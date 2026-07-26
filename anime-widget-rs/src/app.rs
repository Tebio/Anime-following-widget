//! egui 界面 + 应用逻辑。

use std::sync::mpsc::{channel, Receiver};
use std::time::{Duration, Instant};

use egui::{
    Align, CentralPanel, Color32, Context, CursorIcon, FontId, Frame, Label, Layout, Margin,
    RichText, ScrollArea, Sense, Ui, Vec2,
};

#[cfg(windows)]
use tray_icon::menu::{CheckMenuItem, Menu, MenuEvent, MenuId, MenuItem, PredefinedMenuItem};
#[cfg(windows)]
use tray_icon::{TrayIcon, TrayIconBuilder, TrayIconEvent};

use crate::schedule::{fetch_schedule, Entry, WeekSchedule, WEEKDAY_NAMES};
use crate::settings::{Settings, ACCENTS};
use crate::win32::DesktopLayer;

pub const CARD_W: f32 = 320.0;
pub const CARD_H: f32 = 460.0;
const SNAP_DIST: i32 = 20;
const EDGE_MARGIN: i32 = 8;

const NEW_RED: Color32 = Color32::from_rgb(235, 87, 87);
const END_PINK: Color32 = Color32::from_rgb(219, 112, 147);
const SUB_GRAY: Color32 = Color32::from_gray(160);

pub struct WidgetApp {
    settings: Settings,
    schedule: Option<WeekSchedule>,
    selected_day: usize,
    error: Option<String>,

    fetch_rx: Receiver<Result<WeekSchedule, String>>,
    fetch_in_flight: bool,
    next_auto_fetch: Instant,

    settings_open: bool,
    layer: Option<DesktopLayer>,

    #[cfg(windows)]
    _tray: Option<TrayIcon>,
    #[cfg(windows)]
    mi_lock: CheckMenuItem,
    #[cfg(windows)]
    mi_through: CheckMenuItem,
    #[cfg(windows)]
    mi_settings: MenuItem,
    #[cfg(windows)]
    mi_refresh: MenuItem,
    #[cfg(windows)]
    mi_quit: MenuItem,

    dirty: bool,
    last_save: Instant,
}

impl WidgetApp {
    pub fn new(cc: &eframe::CreationContext<'_>) -> Self {
        crate::fonts::install_cjk_fonts(&cc.egui_ctx);

        let settings = Settings::load();
        let selected_day = today_index();
        let schedule = load_cache(); // 离线兜底
        let (_tx, rx) = channel();

        #[cfg(windows)]
        let (tray, mi_lock, mi_through, mi_settings, mi_refresh, mi_quit) = build_tray(&settings);

        let mut app = Self {
            settings,
            schedule,
            selected_day,
            error: None,
            fetch_rx: rx,
            fetch_in_flight: false,
            next_auto_fetch: Instant::now(),
            settings_open: false,
            layer: None,
            #[cfg(windows)]
            _tray: tray,
            #[cfg(windows)]
            mi_lock,
            #[cfg(windows)]
            mi_through,
            #[cfg(windows)]
            mi_settings,
            #[cfg(windows)]
            mi_refresh,
            #[cfg(windows)]
            mi_quit,
            dirty: false,
            last_save: Instant::now(),
        };
        app.start_fetch(&cc.egui_ctx);
        app
    }

    // ---------- 数据 ----------

    fn start_fetch(&mut self, ctx: &Context) {
        if self.fetch_in_flight {
            return;
        }
        self.fetch_in_flight = true;
        let (tx, rx) = channel();
        self.fetch_rx = rx;
        let ctx = ctx.clone();
        std::thread::spawn(move || {
            let result = fetch_schedule();
            let _ = tx.send(result);
            ctx.request_repaint();
        });
    }

    fn poll_fetch(&mut self) {
        if !self.fetch_in_flight {
            return;
        }
        match self.fetch_rx.try_recv() {
            Ok(Ok(sched)) => {
                self.error = None;
                self.fetch_in_flight = false;
                self.next_auto_fetch =
                    Instant::now() + Duration::from_secs(self.settings.refresh_minutes.max(5) * 60);
                save_cache(&sched);
                self.schedule = Some(sched);
            }
            Ok(Err(e)) => {
                self.error = Some(e);
                self.fetch_in_flight = false;
                self.next_auto_fetch = Instant::now() + Duration::from_secs(120);
            }
            Err(std::sync::mpsc::TryRecvError::Empty) => {}
            Err(std::sync::mpsc::TryRecvError::Disconnected) => {
                self.fetch_in_flight = false;
            }
        }
    }

    // ---------- Win32 层 ----------

    fn ensure_layer(&mut self, frame: &eframe::Frame) {
        if self.layer.is_some() {
            return;
        }
        use raw_window_handle::{HasWindowHandle, RawWindowHandle};
        if let Ok(handle) = frame.window_handle() {
            if let RawWindowHandle::Win32(win) = handle.as_raw() {
                let layer = DesktopLayer::attach(win.hwnd.get());
                layer.set_opacity(self.settings.window_opacity);
                layer.set_click_through(self.effective_click_through());
                self.layer = Some(layer);
            }
        }
    }

    fn effective_click_through(&self) -> bool {
        // 设置面板打开时强制可交互，避免把自己锁死
        self.settings.click_through && !self.settings_open
    }

    fn apply_layer_flags(&self) {
        if let Some(layer) = &self.layer {
            layer.set_opacity(self.settings.window_opacity);
            layer.set_click_through(self.effective_click_through());
        }
    }

    fn sync_tray_checks(&self) {
        #[cfg(windows)]
        {
            self.mi_lock.set_checked(self.settings.locked);
            self.mi_through.set_checked(self.settings.click_through);
        }
    }

    /// 拖拽移动 + 松手边缘磁吸。任何 draggable response 都可以传进来。
    fn handle_drag(&mut self, ctx: &Context, response: &egui::Response) {
        if self.settings.locked || self.settings_open {
            return;
        }
        let Some(layer) = &self.layer else { return };

        if response.dragged_by(egui::PointerButton::Primary) {
            let delta = ctx.input(|i| i.pointer.delta());
            if delta != Vec2::ZERO {
                let pp = ctx.pixels_per_point();
                if let Some((x, y, _, _)) = layer.rect() {
                    layer.set_pos(
                        x + (delta.x * pp).round() as i32,
                        y + (delta.y * pp).round() as i32,
                    );
                }
            }
        }
        if response.drag_stopped_by(egui::PointerButton::Primary) {
            let pp = ctx.pixels_per_point();
            if let Some((x, y, w, h)) = layer.rect() {
                let (sw, sh) = layer.screen_size();
                let mut nx = x;
                let mut ny = y;
                if x < SNAP_DIST {
                    nx = EDGE_MARGIN;
                } else if (sw - (x + w)).abs() < SNAP_DIST {
                    nx = sw - w - EDGE_MARGIN;
                }
                if y < SNAP_DIST {
                    ny = EDGE_MARGIN;
                } else if (sh - (y + h)).abs() < SNAP_DIST {
                    ny = sh - h - EDGE_MARGIN;
                }
                if (nx, ny) != (x, y) {
                    layer.set_pos(nx, ny);
                }
                self.settings.pos = Some((nx as f32 / pp, ny as f32 / pp));
                self.dirty = true;
            }
        }
    }

    // ---------- 托盘 ----------

    #[cfg(windows)]
    fn poll_tray(&mut self, ctx: &Context) {
        while let Ok(event) = MenuEvent::receiver().try_recv() {
            let id: &MenuId = &event.id;
            if *id == self.mi_lock.id() {
                self.settings.locked = !self.settings.locked;
                self.sync_tray_checks();
                self.dirty = true;
            } else if *id == self.mi_through.id() {
                self.settings.click_through = !self.settings.click_through;
                self.sync_tray_checks();
                self.apply_layer_flags();
                self.dirty = true;
            } else if *id == self.mi_settings.id() {
                self.settings_open = !self.settings_open;
                self.apply_layer_flags();
            } else if *id == self.mi_refresh.id() {
                self.start_fetch(ctx);
            } else if *id == self.mi_quit.id() {
                self.settings.save();
                ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            }
        }
        while let Ok(event) = TrayIconEvent::receiver().try_recv() {
            if let TrayIconEvent::DoubleClick { .. } = event {
                self.settings_open = !self.settings_open;
                self.apply_layer_flags();
            }
        }
    }

    #[cfg(not(windows))]
    fn poll_tray(&mut self, _ctx: &Context) {}

    fn maybe_save(&mut self) {
        if self.dirty && self.last_save.elapsed() > Duration::from_secs(2) {
            self.settings.save();
            self.dirty = false;
            self.last_save = Instant::now();
        }
    }
}

impl eframe::App for WidgetApp {
    fn update(&mut self, ctx: &Context, frame: &mut eframe::Frame) {
        self.ensure_layer(frame);
        self.poll_tray(ctx);
        self.poll_fetch();
        if !self.fetch_in_flight && Instant::now() >= self.next_auto_fetch {
            self.start_fetch(ctx);
        }

        let accent = {
            let [r, g, b] = self.settings.accent_rgb();
            Color32::from_rgb(r, g, b)
        };
        let d = self.settings.bg_darkness.clamp(0.0, 1.0);
        let bg_rgb = lerp_rgb([52, 56, 66], [12, 14, 18], d);
        let bg_alpha = (150.0 + 105.0 * d).round() as u8;
        let bg = Color32::from_rgba_unmultiplied(bg_rgb[0], bg_rgb[1], bg_rgb[2], bg_alpha);

        let card = Frame::new()
            .fill(bg)
            .corner_radius(14.0)
            .inner_margin(Margin::same(12));

        let panel = CentralPanel::default().frame(card).show(ctx, |ui| {
            self.draw_header(ui, ctx, accent);
            ui.add_space(4.0);
            self.draw_tabs(ui, accent);
            ui.add_space(4.0);
            ui.separator();
            self.draw_list(ui, accent);
        });

        // 空白处：拖拽移动 + 右键菜单
        let bg_resp = panel.response.interact(Sense::click_and_drag());
        self.handle_drag(ctx, &bg_resp);
        self.context_menu(ctx, &bg_resp);

        if self.settings_open {
            self.draw_settings(ctx);
        }

        self.maybe_save();
        // 托盘事件 / 定时刷新需要周期唤醒
        ctx.request_repaint_after(Duration::from_millis(500));
    }

    fn on_exit(&mut self, _gl: Option<&eframe::glow::Context>) {
        self.settings.save();
    }
}

// ---------- UI 绘制 ----------

impl WidgetApp {
    fn draw_header(&mut self, ui: &mut Ui, ctx: &Context, accent: Color32) {
        ui.horizontal(|ui| {
            ui.label(RichText::new("本周放送列表").size(17.0).strong());
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                let gear = ui.add(Label::new(RichText::new("⚙").size(14.0)).sense(Sense::click()));
                if gear.clicked() {
                    self.settings_open = !self.settings_open;
                    self.apply_layer_flags();
                }
                let icon = if self.fetch_in_flight { "…" } else { "⟳" };
                let refresh = ui.add(
                    Label::new(RichText::new(icon).size(14.0).color(accent)).sense(Sense::click()),
                );
                if refresh.clicked() {
                    self.start_fetch(ctx);
                }
                if let Some(sched) = &self.schedule {
                    ui.label(
                        RichText::new(format!("更新于 {}", sched.fetched_at))
                            .size(10.0)
                            .color(Color32::from_gray(120)),
                    );
                }
            });
        });
        if let Some(err) = &self.error {
            ui.label(
                RichText::new(format!("⚠ {err}（显示缓存数据）"))
                    .size(11.0)
                    .color(Color32::from_rgb(230, 126, 98)),
            );
        }
    }

    fn draw_tabs(&mut self, ui: &mut Ui, accent: Color32) {
        ui.horizontal(|ui| {
            for (i, name) in WEEKDAY_NAMES.iter().enumerate() {
                let selected = self.selected_day == i;
                let text = if selected {
                    RichText::new(*name).size(13.0).color(accent).strong()
                } else {
                    RichText::new(*name)
                        .size(13.0)
                        .color(Color32::from_gray(150))
                };
                if ui.selectable_label(selected, text).clicked() {
                    self.selected_day = i;
                }
            }
        });
    }

    fn draw_list(&mut self, ui: &mut Ui, accent: Color32) {
        // 克隆当日条目，避免闭包同时借用 self
        let entries: Option<Vec<Entry>> = self
            .schedule
            .as_ref()
            .and_then(|s| s.days.get(self.selected_day))
            .map(|d| d.entries.clone());

        ScrollArea::vertical()
            .auto_shrink([false, false])
            .show(ui, |ui| match entries {
                None => {
                    ui.add_space(30.0);
                    ui.centered_and_justified(|ui| {
                        ui.label(RichText::new("加载中…").color(Color32::from_gray(140)));
                    });
                }
                Some(ref list) if list.is_empty() => {
                    ui.add_space(30.0);
                    ui.centered_and_justified(|ui| {
                        ui.label(RichText::new("本日无放送").color(Color32::from_gray(140)));
                    });
                }
                Some(ref list) => {
                    for entry in list {
                        self.draw_row(ui, entry, accent);
                    }
                }
            });
    }

    fn draw_row(&mut self, ui: &mut Ui, entry: &Entry, accent: Color32) {
        let row_h = 24.0;
        let row_w = ui.available_width();

        // 右侧文本：「23:00 第04集」或完结
        let mut right = String::new();
        if let Some(t) = &entry.time {
            right.push_str(t);
            right.push(' ');
        }
        right.push_str(&entry.label);
        let right = right.trim().to_string();

        // 精确测量右侧宽度，标题占剩余空间并截断
        let font_sub = FontId::proportional(12.0);
        let right_w = if right.is_empty() && !entry.is_end {
            0.0
        } else {
            let end_w = if entry.is_end {
                ui.painter()
                    .layout_no_wrap("完结".into(), font_sub.clone(), END_PINK)
                    .rect
                    .width()
                    + 8.0
            } else {
                0.0
            };
            let text_w = if right.is_empty() {
                0.0
            } else {
                ui.painter()
                    .layout_no_wrap(right.clone(), font_sub.clone(), SUB_GRAY)
                    .rect
                    .width()
            } + 4.0;
            end_w + text_w
        };
        let new_w = if entry.is_new { 36.0 } else { 0.0 };
        let title_w = (row_w - right_w - new_w - 4.0).max(40.0);

        let resp = ui.horizontal(|row_ui| {
            // 标题（点击 → AGE 搜索；拖动 → 移动卡片）
            let title = row_ui.add_sized(
                [title_w, row_h - 6.0],
                Label::new(RichText::new(&entry.title).size(14.0))
                    .sense(Sense::click_and_drag())
                    .truncate(),
            );
            let title = title.on_hover_text(format!("在 AGE 动漫搜索「{}」", entry.title));
            if title.hovered() {
                row_ui.ctx().set_cursor_icon(CursorIcon::PointingHand);
                // hover 下划线
                let r = title.rect;
                row_ui.painter().line_segment(
                    [r.left_bottom(), r.right_bottom()],
                    egui::Stroke::new(1.0_f32, accent),
                );
            }
            if title.clicked() {
                let _ = open::that(entry.search_url());
            }
            self.handle_drag(row_ui.ctx(), &title);

            if entry.is_new {
                row_ui.label(RichText::new("New!").size(11.0).italics().color(NEW_RED));
            }

            row_ui.with_layout(Layout::right_to_left(Align::Center), |row_ui| {
                if entry.is_end {
                    row_ui.label(RichText::new("完结").size(12.0).italics().color(END_PINK));
                }
                if !right.is_empty() {
                    row_ui.label(RichText::new(&right).size(12.0).color(SUB_GRAY));
                }
            });
        });

        // 行 hover 微高亮
        if resp.response.hovered() {
            ui.painter()
                .rect_filled(resp.response.rect, 6.0, Color32::from_white_alpha(5));
        }
    }

    fn context_menu(&mut self, ctx: &Context, response: &egui::Response) {
        response.context_menu(|ui| {
            if ui.checkbox(&mut self.settings.locked, "锁定位置").changed() {
                self.sync_tray_checks();
                self.dirty = true;
            }
            if ui
                .checkbox(&mut self.settings.click_through, "鼠标穿透")
                .changed()
            {
                self.sync_tray_checks();
                self.apply_layer_flags();
                self.dirty = true;
            }
            ui.separator();
            if ui.button("设置…").clicked() {
                self.settings_open = true;
                self.apply_layer_flags();
                ui.close_menu();
            }
            if ui.button("立即刷新").clicked() {
                self.start_fetch(ctx);
                ui.close_menu();
            }
            ui.separator();
            if ui.button("退出").clicked() {
                self.settings.save();
                ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            }
        });
    }

    fn draw_settings(&mut self, ctx: &Context) {
        let mut open = self.settings_open;
        egui::Window::new("设置")
            .open(&mut open)
            .collapsible(false)
            .resizable(false)
            .anchor(egui::Align2::RIGHT_TOP, Vec2::new(-10.0, 10.0))
            .show(ctx, |ui| {
                ui.label("整体透明度");
                if ui
                    .add(egui::Slider::new(
                        &mut self.settings.window_opacity,
                        0.3..=1.0,
                    ))
                    .changed()
                {
                    self.apply_layer_flags();
                    self.dirty = true;
                }

                ui.label("背景深浅");
                if ui
                    .add(egui::Slider::new(&mut self.settings.bg_darkness, 0.0..=1.0))
                    .changed()
                {
                    self.dirty = true;
                }

                ui.add_space(6.0);
                ui.label("强调色");
                ui.horizontal(|ui| {
                    for (i, (name, rgb)) in ACCENTS.iter().enumerate() {
                        let c = Color32::from_rgb(rgb[0], rgb[1], rgb[2]);
                        let selected = self.settings.accent == i;
                        let resp = ui
                            .add(
                                egui::Button::new(
                                    RichText::new(if selected { "●" } else { " " }).color(c),
                                )
                                .fill(c.gamma_multiply(if selected { 0.45 } else { 0.2 }))
                                .min_size(Vec2::new(28.0, 24.0)),
                            )
                            .on_hover_text(*name);
                        if resp.clicked() {
                            self.settings.accent = i;
                            self.dirty = true;
                        }
                    }
                });

                ui.add_space(6.0);
                if ui
                    .checkbox(&mut self.settings.locked, "锁定位置（禁止拖拽）")
                    .changed()
                {
                    self.sync_tray_checks();
                    self.dirty = true;
                }
                if ui
                    .checkbox(&mut self.settings.click_through, "鼠标穿透（点击穿过卡片）")
                    .changed()
                {
                    self.sync_tray_checks();
                    self.apply_layer_flags();
                    self.dirty = true;
                }

                ui.add_space(6.0);
                ui.horizontal(|ui| {
                    ui.label("自动刷新（分钟）");
                    if ui
                        .add(
                            egui::DragValue::new(&mut self.settings.refresh_minutes).range(5..=720),
                        )
                        .changed()
                    {
                        self.next_auto_fetch = Instant::now()
                            + Duration::from_secs(self.settings.refresh_minutes.max(5) * 60);
                        self.dirty = true;
                    }
                });

                ui.add_space(4.0);
                ui.label(
                    RichText::new("提示：穿透状态下双击托盘图标可重新打开设置")
                        .size(10.0)
                        .color(Color32::from_gray(130)),
                );
            });
        if open != self.settings_open {
            self.settings_open = open;
            self.apply_layer_flags();
        }
    }
}

// ---------- 小工具 ----------

fn today_index() -> usize {
    use chrono::Datelike;
    match chrono::Local::now().weekday() {
        chrono::Weekday::Mon => 0,
        chrono::Weekday::Tue => 1,
        chrono::Weekday::Wed => 2,
        chrono::Weekday::Thu => 3,
        chrono::Weekday::Fri => 4,
        chrono::Weekday::Sat => 5,
        chrono::Weekday::Sun => 6,
    }
}

fn lerp_rgb(a: [u8; 3], b: [u8; 3], t: f32) -> [u8; 3] {
    let f = |x: u8, y: u8| (x as f32 + (y as f32 - x as f32) * t).round() as u8;
    [f(a[0], b[0]), f(a[1], b[1]), f(a[2], b[2])]
}

fn save_cache(sched: &WeekSchedule) {
    if let Ok(json) = serde_json::to_string(sched) {
        let _ = std::fs::write(crate::settings::cache_path(), json);
    }
}

fn load_cache() -> Option<WeekSchedule> {
    let s = std::fs::read_to_string(crate::settings::cache_path()).ok()?;
    serde_json::from_str(&s).ok()
}

#[cfg(windows)]
fn build_tray(
    settings: &Settings,
) -> (
    Option<TrayIcon>,
    CheckMenuItem,
    CheckMenuItem,
    MenuItem,
    MenuItem,
    MenuItem,
) {
    let mi_lock = CheckMenuItem::new("锁定位置", true, settings.locked, None);
    let mi_through = CheckMenuItem::new("鼠标穿透", true, settings.click_through, None);
    let mi_settings = MenuItem::new("设置…", true, None);
    let mi_refresh = MenuItem::new("立即刷新", true, None);
    let mi_quit = MenuItem::new("退出", true, None);
    let menu = Menu::new();
    let _ = menu.append_items(&[
        &mi_lock,
        &mi_through,
        &mi_settings,
        &mi_refresh,
        &PredefinedMenuItem::separator(),
        &mi_quit,
    ]);
    let tray = TrayIconBuilder::new()
        .with_menu(Box::new(menu))
        .with_tooltip("追番小组件")
        .with_icon(make_tray_icon(settings.accent_rgb()))
        .build()
        .ok();
    (tray, mi_lock, mi_through, mi_settings, mi_refresh, mi_quit)
}

/// 程序化生成 32x32 托盘图标：圆角方块 + 三个深色圆点
#[cfg(windows)]
fn make_tray_icon(rgb: [u8; 3]) -> tray_icon::Icon {
    const S: usize = 32;
    let mut rgba = vec![0u8; S * S * 4];
    let r = 7.0f32;
    for y in 0..S {
        for x in 0..S {
            let fx = x as f32 + 0.5;
            let fy = y as f32 + 0.5;
            if !rounded_rect(fx, fy, 2.0, 2.0, (S - 4) as f32, (S - 4) as f32, r) {
                continue;
            }
            let idx = (y * S + x) * 4;
            rgba[idx] = rgb[0];
            rgba[idx + 1] = rgb[1];
            rgba[idx + 2] = rgb[2];
            rgba[idx + 3] = 255;
            for cx in [10.0f32, 16.0, 22.0] {
                let dx = fx - cx;
                let dy = fy - 16.0;
                if dx * dx + dy * dy < 2.2 * 2.2 {
                    rgba[idx] = 20;
                    rgba[idx + 1] = 22;
                    rgba[idx + 2] = 26;
                    rgba[idx + 3] = 255;
                }
            }
        }
    }
    tray_icon::Icon::from_rgba(rgba, S as u32, S as u32).expect("tray icon")
}

#[cfg(windows)]
fn rounded_rect(px: f32, py: f32, x: f32, y: f32, w: f32, h: f32, r: f32) -> bool {
    let in_core = (px >= x + r && px <= x + w - r && py >= y && py <= y + h)
        || (px >= x && px <= x + w && py >= y + r && py <= y + h - r);
    if in_core {
        return true;
    }
    for (ccx, ccy) in [
        (x + r, y + r),
        (x + w - r, y + r),
        (x + r, y + h - r),
        (x + w - r, y + h - r),
    ] {
        let dx = px - ccx;
        let dy = py - ccy;
        if dx * dx + dy * dy <= r * r {
            return true;
        }
    }
    false
}
