//! AGE 动漫「本周放送列表」抓取与解析。
//!
//! 数据源：https://www.agedm.io/ 首页服务端渲染的周表（7 个 tab pane，周一~周日）。
//! 每条结构：
//!   <li class="[episode_end]">
//!     <a href="/detail/{id}">标题</a>
//!     <div class="title_new">New!|空</div>
//!     <div class="title_sub">[HH:MM ]第NN集|已完结|PV...</div>
//!   </li>

use scraper::{Html, Selector};

pub const AGEDM_HOME: &str = "https://www.agedm.io/";
pub const AGEDM_SEARCH: &str = "https://www.agedm.io/search?query=";

/// 数据源镜像，按优先级failover（agedm 常年换域名，主站挂了用镜像）
pub const MIRRORS: [&str; 2] = ["https://www.agedm.io", "https://www.age.tv"];

/// 一周 7 天。index 0=周一 … 6=周日。
#[derive(Debug, Clone, Default, serde::Serialize, serde::Deserialize)]
#[serde(default)]
pub struct WeekSchedule {
    /// 实际抓取成功的镜像 base（拼链接也用它，避免主站挂了点不开）
    pub base: String,
    pub days: Vec<DaySchedule>,
    /// 抓取成功时间（RFC3339 本地）
    pub fetched_at: String,
}

#[derive(Debug, Clone, Default, serde::Serialize, serde::Deserialize)]
pub struct DaySchedule {
    /// 0=周一 … 6=周日
    pub weekday: usize,
    pub entries: Vec<Entry>,
}

#[derive(Debug, Clone, Default, serde::Serialize, serde::Deserialize)]
pub struct Entry {
    pub title: String,
    /// agedm detail id，如 "20260168"
    pub detail_id: String,
    pub is_new: bool,
    /// 已完结（li class 含 episode_end 或副标题含完结）
    pub is_end: bool,
    /// 放送时间 "23:00"，可能为空
    pub time: Option<String>,
    /// 副标题去掉时间后的部分，如 "第04集" / "已完结" / "PV"
    pub label: String,
}

impl Entry {
    /// 点击标题跳转：AGE 搜索页（主站）
    pub fn search_url(&self) -> String {
        self.search_url_with(MIRRORS[0])
    }

    pub fn search_url_with(&self, base: &str) -> String {
        use percent_encoding::{utf8_percent_encode, NON_ALPHANUMERIC};
        format!(
            "{}/search?query={}",
            base.trim_end_matches('/'),
            utf8_percent_encode(&self.title, NON_ALPHANUMERIC)
        )
    }

    /// AGE 详情页（主站）
    pub fn detail_url(&self) -> String {
        self.detail_url_with(MIRRORS[0])
    }

    pub fn detail_url_with(&self, base: &str) -> String {
        format!("{}/detail/{}", base.trim_end_matches('/'), self.detail_id)
    }
}

pub const WEEKDAY_NAMES: [&str; 7] = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];

/// 解析 agedm 首页 HTML。page pane id: week-1..week-6 = 周一~周六，week-0 = 周日。
pub fn parse_schedule(html: &str, fetched_at: String) -> WeekSchedule {
    parse_schedule_with_base(html, fetched_at, MIRRORS[0])
}

pub fn parse_schedule_with_base(html: &str, fetched_at: String, base: &str) -> WeekSchedule {
    let doc = Html::parse_document(html);
    let mut days = Vec::with_capacity(7);

    for weekday in 0..7 {
        // agedm pane id: 周一=1 … 周六=6，周日=0
        let pane_id = if weekday == 6 { 0 } else { weekday + 1 };
        let pane_sel = Selector::parse(&format!("#week-{}-pane", pane_id)).unwrap();
        let li_sel = Selector::parse("li").unwrap();
        let a_sel = Selector::parse("a[href*=\"/detail/\"]").unwrap();
        let new_sel = Selector::parse(".title_new").unwrap();
        let sub_sel = Selector::parse(".title_sub").unwrap();

        let mut entries = Vec::new();
        for pane in doc.select(&pane_sel) {
            for li in pane.select(&li_sel) {
                let is_end_class = li
                    .value()
                    .attr("class")
                    .map(|c| c.contains("episode_end"))
                    .unwrap_or(false);

                let Some(a) = li.select(&a_sel).next() else {
                    continue;
                };
                let title = a.text().collect::<String>().trim().to_string();
                if title.is_empty() {
                    continue;
                }
                let href = a.value().attr("href").unwrap_or("");
                let detail_id = href
                    .rsplit("/detail/")
                    .next()
                    .unwrap_or("")
                    .trim_matches('/')
                    .to_string();

                let is_new = li
                    .select(&new_sel)
                    .next()
                    .map(|n| n.text().collect::<String>().contains("New"))
                    .unwrap_or(false);

                let sub_raw = li
                    .select(&sub_sel)
                    .next()
                    .map(|s| s.text().collect::<String>().trim().to_string())
                    .unwrap_or_default();
                let (time, label) = split_time(&sub_raw);
                let is_end = is_end_class || label.contains("完结");

                entries.push(Entry {
                    title,
                    detail_id,
                    is_new,
                    is_end,
                    time,
                    label,
                });
            }
        }
        days.push(DaySchedule { weekday, entries });
    }

    WeekSchedule {
        base: base.trim_end_matches('/').to_string(),
        days,
        fetched_at,
    }
}

/// "23:00 第04集" -> (Some("23:00"), "第04集")；"第11集(完结)" -> (None, ...)
fn split_time(sub: &str) -> (Option<String>, String) {
    let sub = sub.trim();
    if let Some(idx) = sub.find(char::is_whitespace) {
        let (head, tail) = sub.split_at(idx);
        if is_hhmm(head) {
            return (Some(head.to_string()), tail.trim().to_string());
        }
    }
    (None, sub.to_string())
}

fn is_hhmm(s: &str) -> bool {
    let parts: Vec<&str> = s.split(':').collect();
    parts.len() == 2
        && parts[0].len() == 2
        && parts[1].len() == 2
        && parts.iter().all(|p| p.chars().all(|c| c.is_ascii_digit()))
}

/// 抓取周表：按 MIRRORS 顺序 failover，第一个解析出数据的镜像胜出（阻塞，放后台线程用）。
/// 自动应用 Windows 系统代理 / 环境变量代理（ureq 默认不走系统代理，
/// 国内直连 agedm 会被 DNS 污染掐掉）。
pub fn fetch_schedule() -> Result<WeekSchedule, String> {
    let proxy = crate::proxy::detect_proxy();
    let mut builder = ureq::AgentBuilder::new()
        .timeout_connect(std::time::Duration::from_secs(8))
        .timeout_read(std::time::Duration::from_secs(15));
    if let Some(p) = &proxy {
        match ureq::Proxy::new(p.clone()) {
            Ok(px) => builder = builder.proxy(px),
            Err(e) => eprintln!("[anime-widget] 代理地址无效 {p}: {e}"),
        }
    }
    let agent = builder.build();
    let via = proxy.as_deref().unwrap_or("直连");

    let mut errors = Vec::new();
    for base in MIRRORS {
        match fetch_one(&agent, base) {
            Ok(sched) => return Ok(sched),
            Err(e) => {
                eprintln!("[anime-widget] 镜像 {base} 失败({via}): {e}");
                errors.push(format!("{base} → {e}"));
            }
        }
    }
    Err(format!("全部镜像不可用（{via}）：\n{}", errors.join("\n")))
}

fn fetch_one(agent: &ureq::Agent, base: &str) -> Result<WeekSchedule, String> {
    let resp = agent
        .get(base)
        .set(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 \
             (KHTML, like Gecko) Chrome/126.0 Safari/537.36",
        )
        .set("Accept-Language", "zh-CN,zh;q=0.9")
        .call()
        .map_err(|e| format!("请求失败: {e}"))?;
    let html = resp.into_string().map_err(|e| format!("读取失败: {e}"))?;
    let fetched_at = chrono::Local::now().format("%Y-%m-%d %H:%M").to_string();
    let sched = parse_schedule_with_base(&html, fetched_at, base);
    if sched.days.iter().all(|d| d.entries.is_empty()) {
        return Err("解析结果为空（页面结构可能已变更）".into());
    }
    Ok(sched)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn split_time_works() {
        assert_eq!(
            split_time("23:00 第04集"),
            (Some("23:00".into()), "第04集".into())
        );
        assert_eq!(split_time("第11集(完结)"), (None, "第11集(完结)".into()));
        assert_eq!(
            split_time("00:00 已完结"),
            (Some("00:00".into()), "已完结".into())
        );
        assert_eq!(split_time(""), (None, "".into()));
    }

    #[test]
    fn search_url_encodes() {
        let e = Entry {
            title: "凡人修仙传".into(),
            ..Default::default()
        };
        assert!(e.search_url().starts_with(AGEDM_SEARCH));
        assert!(e.search_url().contains("%E5%87%A1%E4%BA%BA"));
    }
}
