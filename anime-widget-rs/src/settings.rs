use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WidgetSettings {
    #[serde(default)]
    pub feed_source: String,
    #[serde(default = "default_true")]
    pub always_on_top: bool,
    #[serde(default = "default_true")]
    pub dark_theme: bool,
    #[serde(default)]
    pub accent_index: u8,
    #[serde(default = "default_opacity")]
    pub opacity: f32,
    #[serde(default = "default_refresh")]
    pub refresh_minutes: u32,
    #[serde(default = "default_sort")]
    pub sort_mode: u8,
    #[serde(default)]
    pub only_today: bool,
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
fn default_refresh() -> u32 {
    15
}
fn default_sort() -> u8 {
    1
}

impl Default for WidgetSettings {
    fn default() -> Self {
        Self {
            feed_source: String::new(),
            always_on_top: true,
            dark_theme: true,
            accent_index: 0,
            opacity: 0.95,
            refresh_minutes: 15,
            sort_mode: 1,
            only_today: false,
            window_x: None,
            window_y: None,
            window_w: Some(380.0),
            window_h: Some(560.0),
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
        let Ok(text) = fs::read_to_string(&path) else {
            return Self::default();
        };
        let Ok(mut s) = serde_json::from_str::<WidgetSettings>(&text) else {
            return Self::default();
        };
        if s.refresh_minutes < 1 {
            s.refresh_minutes = 1;
        }
        s.opacity = s.opacity.clamp(0.5, 1.0);
        s
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
