use crate::models::FeedData;
use anyhow::{bail, Context, Result};
use std::fs;
use std::path::Path;

/// 内置示例（与 feed.sample.json 同步，便于单文件运行）
pub const EMBEDDED_SAMPLE: &str = include_str!("../feed.sample.json");

pub fn load_feed(source: &str) -> Result<FeedData> {
    let source = source.trim();
    if source.is_empty() {
        return parse_json(EMBEDDED_SAMPLE);
    }

    if source.starts_with("http://") || source.starts_with("https://") {
        let body = ureq::get(source)
            .set("User-Agent", "AnimeFollowingWidget/1.2 (Rust)")
            .timeout(std::time::Duration::from_secs(15))
            .call()
            .with_context(|| format!("请求失败: {source}"))?
            .into_string()
            .context("读取响应体失败")?;
        return parse_json(&body);
    }

    // 本地路径
    let path = Path::new(source);
    if !path.exists() {
        // 尝试相对可执行文件目录
        if let Ok(exe) = std::env::current_exe() {
            if let Some(dir) = exe.parent() {
                let alt = dir.join(source);
                if alt.exists() {
                    let text = fs::read_to_string(&alt)?;
                    return parse_json(&text);
                }
            }
        }
        bail!("数据文件不存在: {source}");
    }
    let text = fs::read_to_string(path).with_context(|| format!("读取失败: {source}"))?;
    parse_json(&text)
}

fn parse_json(text: &str) -> Result<FeedData> {
    serde_json::from_str(text).context("JSON 解析失败")
}
