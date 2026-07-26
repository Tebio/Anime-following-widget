use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WidgetSettings {
    /// 数据源：HTTP(S) URL 或本地路径；空则用内置示例
    #[serde(default)]
    pub feed_source: String,
    #[serde(default = "default_true")]
    pub always_on_top: bool,
    #[serde(default)]
    pub dark_theme: bool,
    /// 0=紫 1=青 2=绿 3=粉 4=橙
    #[serde(default)]
    pub accent_index: u8,
    #[serde(default = "default_opacity")]
    pub opacity: f32,
    #[serde(default)]
    pub window_x: Option<f32>,
    #[serde(default)]
    pub window_y: Option<f32>,
    #[serde(default)]
    pub window_w: Option<f32>,
    #[serde(default)]
    pub window_h: Option<f32>,
}

fn default_true() -> bool {
    true
}
fn default_opacity() -> f32 {
    0.95
}

impl Default for WidgetSettings {
    fn default() -> Self {
        Self {
            feed_source: String::new(),
            always_on_top: true,
            dark_theme: true,
            accent_index: 0,
            opacity: 0.95,
            window_x: None,
            window_y: None,
            window_w: Some(360.0),
            window_h: Some(520.0),
        }
    }
}

pub fn settings_path() -> PathBuf {
    dirs::data_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("AnimeFollowingWidget")
        .join("settings.json")
}

impl WidgetSettings {
    pub fn load() -> Self {
        let path = settings_path();
        if !path.exists() {
            return Self::default();
        }
        match fs::read_to_string(&path)
            .ok()
            .and_then(|s| serde_json::from_str(&s).ok())
        {
            Some(s) => s,
            None => Self::default(),
        }
    }

    pub fn save(&self) -> Result<()> {
        let path = settings_path();
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).context("创建配置目录失败")?;
        }
        let json = serde_json::to_string_pretty(self)?;
        fs::write(&path, json).context("写入 settings.json 失败")?;
        Ok(())
    }
}
