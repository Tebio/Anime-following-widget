//! 设置持久化：%APPDATA%\AnimeFollowingWidget\settings.json
//! 另缓存最近一次成功抓取的周表 cache.json，离线启动也有数据。

use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

/// 点击番名的行为
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ClickTarget {
    /// 打开番剧详情页（精确直达，推荐）
    Detail,
    /// 打开 AGE 搜索结果页
    Search,
}

impl Default for ClickTarget {
    fn default() -> Self {
        ClickTarget::Detail
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct Settings {
    /// 强调色索引（ACCENTS）
    pub accent: usize,
    /// 整体窗口不透明度 0.3~1.0
    pub window_opacity: f32,
    /// 背景深浅 0.0(最浅)~1.0(最深)
    pub bg_darkness: f32,
    /// 锁定位置（禁止拖拽）
    pub locked: bool,
    /// 鼠标穿透
    pub click_through: bool,
    /// 上次窗口位置（逻辑像素）
    pub pos: Option<(f32, f32)>,
    /// 自动刷新间隔（分钟）
    pub refresh_minutes: u64,
    /// 点击番名打开详情页还是搜索页
    pub click_target: ClickTarget,
    /// 桌面嵌入方式
    pub embed_mode: crate::win32::EmbedMode,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            accent: 0,
            window_opacity: 0.95,
            bg_darkness: 0.85,
            locked: false,
            click_through: false,
            pos: None,
            refresh_minutes: 30,
            click_target: ClickTarget::default(),
            embed_mode: crate::win32::EmbedMode::default(),
        }
    }
}

/// 强调色预设（RGB）
pub const ACCENTS: [(&str, [u8; 3]); 5] = [
    ("青绿", [45, 212, 191]),
    ("香槟金", [229, 192, 123]),
    ("雾紫", [179, 157, 219]),
    ("樱粉", [244, 143, 177]),
    ("暖橙", [255, 171, 145]),
];

fn config_dir() -> PathBuf {
    let base = dirs::config_dir()
        .or_else(dirs::home_dir)
        .unwrap_or_else(|| PathBuf::from("."));
    base.join("AnimeFollowingWidget")
}

fn settings_path() -> PathBuf {
    config_dir().join("settings.json")
}

pub fn cache_path() -> PathBuf {
    config_dir().join("cache.json")
}

impl Settings {
    pub fn load() -> Self {
        fs::read_to_string(settings_path())
            .ok()
            .and_then(|s| serde_json::from_str(&s).ok())
            .unwrap_or_default()
    }

    pub fn save(&self) {
        let dir = config_dir();
        let _ = fs::create_dir_all(&dir);
        if let Ok(json) = serde_json::to_string_pretty(self) {
            let _ = fs::write(settings_path(), json);
        }
    }

    pub fn accent_rgb(&self) -> [u8; 3] {
        ACCENTS
            .get(self.accent)
            .map(|a| a.1)
            .unwrap_or(ACCENTS[0].1)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn roundtrip() {
        let s = Settings::default();
        let json = serde_json::to_string(&s).unwrap();
        let back: Settings = serde_json::from_str(&json).unwrap();
        assert_eq!(back.accent, 0);
        assert!((back.window_opacity - 0.95).abs() < f32::EPSILON);
        assert_eq!(back.refresh_minutes, 30);
    }

    #[test]
    fn missing_fields_defaulted() {
        let back: Settings = serde_json::from_str("{}").unwrap();
        assert!((back.window_opacity - 0.95).abs() < f32::EPSILON);
    }
}
