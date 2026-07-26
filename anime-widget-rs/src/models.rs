use serde::{Deserialize, Serialize};

/// 数据源根对象
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FeedData {
    #[serde(default = "default_title")]
    pub title: String,
    #[serde(default)]
    pub updated_at: Option<String>,
    #[serde(default)]
    pub items: Vec<FeedItem>,
}

fn default_title() -> String {
    "本周放送列表".into()
}

impl Default for FeedData {
    fn default() -> Self {
        Self {
            title: default_title(),
            updated_at: None,
            items: Vec::new(),
        }
    }
}

/// 单个番剧条目
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FeedItem {
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub badge: String,
    #[serde(default)]
    pub time: String,
    #[serde(default)]
    pub episode: String,
    /// 最新集数（数字）
    #[serde(default)]
    pub latest_episode: u32,
    #[serde(default)]
    pub platform: String,
    /// 直接播放链接（备用）
    #[serde(default)]
    pub watch_url: String,
    /// AgeDM 动漫 ID，如 20200283
    #[serde(default)]
    pub anime_id: String,
    /// 季度编号（从 1 开始）
    #[serde(default = "default_season")]
    pub season_number: u32,
    /// 更新星期：0=周日 … 6=周六
    #[serde(default)]
    pub update_weekday: Option<u8>,
    #[serde(default)]
    pub notes: String,
}

fn default_season() -> u32 {
    1
}

impl FeedItem {
    /// 从 episode 文本解析集数，如 "第184集"
    pub fn resolved_episode(&self) -> u32 {
        if self.latest_episode > 0 {
            return self.latest_episode;
        }
        parse_episode_number(&self.episode)
    }

    pub fn weekday_text(&self) -> &'static str {
        match self.update_weekday {
            Some(0) => "周日",
            Some(1) => "周一",
            Some(2) => "周二",
            Some(3) => "周三",
            Some(4) => "周四",
            Some(5) => "周五",
            Some(6) => "周六",
            _ => "未知",
        }
    }

    pub fn is_today_update(&self) -> bool {
        use chrono::{Datelike, Local};
        let today = Local::now().weekday().num_days_from_sunday() as u8;
        self.update_weekday == Some(today)
    }
}

pub fn parse_episode_number(episode_text: &str) -> u32 {
    let clean = episode_text
        .replace('第', "")
        .replace('集', "")
        .replace('话', "")
        .trim()
        .to_string();
    clean.parse::<u32>().unwrap_or(1).max(1)
}
