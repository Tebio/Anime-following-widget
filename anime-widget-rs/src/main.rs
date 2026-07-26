#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod agedm;
mod app;
mod feed;
mod fonts;
mod models;
mod settings;
mod util;

use app::AnimeWidgetApp;
use eframe::egui;

fn main() -> eframe::Result<()> {
    let settings = settings::WidgetSettings::load();
    let w = settings.window_w.unwrap_or(380.0).clamp(280.0, 1200.0);
    let h = settings.window_h.unwrap_or(560.0).clamp(320.0, 1600.0);

    let mut viewport = egui::ViewportBuilder::default()
        .with_inner_size([w, h])
        .with_min_inner_size([280.0, 320.0])
        .with_decorations(false)
        .with_transparent(true)
        .with_resizable(true)
        .with_taskbar(true)
        .with_window_level(if settings.always_on_top {
            egui::WindowLevel::AlwaysOnTop
        } else {
            egui::WindowLevel::Normal
        })
        .with_title("Anime Following Widget");

    if let (Some(x), Some(y)) = (settings.window_x, settings.window_y) {
        viewport = viewport.with_position([x, y]);
    }

    let options = eframe::NativeOptions {
        viewport,
        centered: settings.window_x.is_none(),
        ..Default::default()
    };

    eframe::run_native(
        "Anime Following Widget",
        options,
        Box::new(|cc| Ok(Box::new(AnimeWidgetApp::new(cc)))),
    )
}
