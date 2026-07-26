#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

use anime_widget::app::WidgetApp;
use anime_widget::settings;

fn main() -> eframe::Result<()> {
    let settings = settings::Settings::load();

    let mut viewport = egui::ViewportBuilder::default()
        .with_title("追番小组件")
        .with_decorations(false)
        .with_transparent(true)
        .with_inner_size([anime_widget::app::CARD_W, anime_widget::app::CARD_H])
        .with_min_inner_size([260.0, 300.0])
        .with_resizable(true)
        .with_taskbar(false)
        .with_window_level(egui::WindowLevel::AlwaysOnBottom);

    if let Some((x, y)) = settings.pos {
        viewport = viewport.with_position([x, y]);
    }

    let options = eframe::NativeOptions {
        viewport,
        ..Default::default()
    };

    eframe::run_native(
        "AnimeWidget",
        options,
        Box::new(|cc| Ok(Box::new(WidgetApp::new(cc)))),
    )
}
