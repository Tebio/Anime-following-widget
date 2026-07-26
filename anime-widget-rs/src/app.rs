use crate::agedm;
use crate::feed;
use crate::models::{FeedData, FeedItem};
use crate::settings::WidgetSettings;
use crate::util::{expire_toast, truncate_middle};
use eframe::egui::{self, Align, Color32, Frame, Key, Layout, Margin, RichText, Sense, Stroke, Vec2};
use std::time::{Duration, Instant};

const ACCENTS: [Color32; 5] = [
    Color32::from_rgb(167, 139, 250),
    Color32::from_rgb(34, 211, 238),
    Color32::from_rgb(74, 222, 128),
    Color32::from_rgb(244, 114, 182),
    Color32::from_rgb(251, 146, 60),
];
const SORT_LABELS: [&str; 3] = ["原始顺序", "今日优先", "按标题"];

pub struct AnimeWidgetApp {
    settings: WidgetSettings,
    feed: FeedData,
    status: String,
    show_settings: bool,
    feed_source_edit: String,
    search: String,
    last_refresh: Instant,
    last_tick: Instant,
    last_bounds_save: Instant,
    error_toast: Option<(String, Instant)>,
    success_toast: Option<(String, Instant)>,
    loading: bool,
}

impl AnimeWidgetApp {
    pub fn new(cc: &eframe::CreationContext<'_>) -> Self {
        let settings = WidgetSettings::load();
        let mut style = (*cc.egui_ctx.style()).clone();
        style.spacing.item_spacing = Vec2::new(8.0, 6.0);
        style.interaction.selectable_labels = false;
        cc.egui_ctx.set_style(style);
        let mut app = Self {
            feed_source_edit: settings.feed_source.clone(),
            settings,
            feed: FeedData::default(),
            status: "正在加载…".into(),
            show_settings: false,
            search: String::new(),
            last_refresh: Instant::now() - Duration::from_secs(3600),
            last_tick: Instant::now(),
            last_bounds_save: Instant::now(),
            error_toast: None,
            success_toast: None,
            loading: false,
        };
        app.reload_feed();
        app
    }

    fn accent(&self) -> Color32 {
        ACCENTS[(self.settings.accent_index as usize) % ACCENTS.len()]
    }

    fn reload_feed(&mut self) {
        self.loading = true;
        match feed::load_feed(&self.settings.feed_source) {
            Ok(data) => {
                let src = if self.settings.feed_source.trim().is_empty() {
                    "内置示例".into()
                } else {
                    truncate_middle(&self.settings.feed_source, 42)
                };
                let updated = data
                    .updated_at
                    .as_deref()
                    .map(|s| format!(" · 同步 {s}"))
                    .unwrap_or_default();
                self.status = format!("共 {} 部 · {src}{updated}", data.items.len());
                self.feed = data;
                self.last_refresh = Instant::now();
            }
            Err(e) => {
                self.status = "加载失败，已回退示例".into();
                if let Ok(data) = feed::load_feed("") {
                    self.feed = data;
                }
                self.error_toast = Some((format!("{e}"), Instant::now()));
            }
        }
        self.loading = false;
    }

    fn item_url(item: &FeedItem) -> Result<String, String> {
        agedm::resolve_watch_url(
            &item.anime_id,
            item.season_number,
            item.resolved_episode(),
            &item.watch_url,
        )
    }

    fn open_item(&mut self, item: &FeedItem) {
        match Self::item_url(item) {
            Ok(url) => {
                if let Err(e) = open::that(&url) {
                    self.error_toast = Some((format!("无法打开链接: {e}\n{url}"), Instant::now()));
                }
            }
            Err(e) => self.error_toast = Some((e, Instant::now())),
        }
    }

    fn copy_item_url(&mut self, ctx: &egui::Context, item: &FeedItem) {
        match Self::item_url(item) {
            Ok(url) => {
                ctx.copy_text(url.clone());
                self.success_toast = Some((format!("已复制: {url}"), Instant::now()));
            }
            Err(e) => self.error_toast = Some((e, Instant::now())),
        }
    }

    fn filtered_items(&self) -> Vec<FeedItem> {
        let q = self.search.trim().to_lowercase();
        let mut items: Vec<FeedItem> = self
            .feed
            .items
            .iter()
            .filter(|it| {
                if self.settings.only_today && !it.is_today_update() {
                    return false;
                }
                q.is_empty()
                    || it.title.to_lowercase().contains(&q)
                    || it.platform.to_lowercase().contains(&q)
                    || it.notes.to_lowercase().contains(&q)
                    || it.badge.to_lowercase().contains(&q)
            })
            .cloned()
            .collect();
        match self.settings.sort_mode {
            1 => items.sort_by(|a, b| {
                b.is_today_update()
                    .cmp(&a.is_today_update())
                    .then_with(|| a.title.cmp(&b.title))
            }),
            2 => items.sort_by(|a, b| a.title.cmp(&b.title)),
            _ => {}
        }
        items
    }

    fn apply_theme(&self, ctx: &egui::Context) {
        let mut visuals = if self.settings.dark_theme {
            egui::Visuals::dark()
        } else {
            egui::Visuals::light()
        };
        visuals.window_rounding = egui::Rounding::same(12.0);
        visuals.widgets.hovered.bg_fill = self.accent().gamma_multiply(0.25);
        visuals.panel_fill = if self.settings.dark_theme {
            Color32::from_rgba_unmultiplied(22, 22, 28, 230)
        } else {
            Color32::from_rgba_unmultiplied(248, 248, 252, 240)
        };
        ctx.set_visuals(visuals);
    }

    fn save_bounds_if_needed(&mut self, ctx: &egui::Context) {
        if self.last_bounds_save.elapsed() < Duration::from_secs(2) {
            return;
        }
        self.last_bounds_save = Instant::now();
        let (pos, size) = ctx.input(|i| {
            let vp = i.viewport();
            (
                vp.outer_rect.map(|r| (r.min.x, r.min.y)),
                vp.inner_rect.map(|r| (r.width(), r.height())),
            )
        });
        let mut dirty = false;
        if let Some((x, y)) = pos {
            if self.settings.window_x != Some(x) || self.settings.window_y != Some(y) {
                self.settings.window_x = Some(x);
                self.settings.window_y = Some(y);
                dirty = true;
            }
        }
        if let Some((w, h)) = size {
            if w > 100.0
                && h > 100.0
                && (self.settings.window_w != Some(w) || self.settings.window_h != Some(h))
            {
                self.settings.window_w = Some(w);
                self.settings.window_h = Some(h);
                dirty = true;
            }
        }
        if dirty {
            let _ = self.settings.save();
        }
    }

    fn handle_keys(&mut self, ctx: &egui::Context) {
        let mut do_refresh = false;
        let mut do_close = false;
        let mut toggle_theme = false;
        let mut toggle_settings = false;
        ctx.input(|i| {
            if i.key_pressed(Key::F5) || (i.modifiers.ctrl && i.key_pressed(Key::R)) {
                do_refresh = true;
            }
            if i.key_pressed(Key::Escape) {
                if self.show_settings {
                    toggle_settings = true;
                } else {
                    do_close = true;
                }
            }
            if i.modifiers.ctrl && i.key_pressed(Key::Comma) {
                toggle_settings = true;
            }
            if i.modifiers.ctrl && i.key_pressed(Key::T) {
                toggle_theme = true;
            }
        });
        if do_refresh {
            self.reload_feed();
        }
        if toggle_theme {
            self.settings.dark_theme = !self.settings.dark_theme;
            let _ = self.settings.save();
        }
        if toggle_settings {
            self.show_settings = !self.show_settings;
            self.feed_source_edit = self.settings.feed_source.clone();
        }
        if do_close {
            let _ = self.settings.save();
            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
        }
    }
}

impl eframe::App for AnimeWidgetApp {
    fn clear_color(&self, _: &egui::Visuals) -> [f32; 4] {
        [0.0, 0.0, 0.0, 0.0]
    }

    fn update(&mut self, ctx: &egui::Context, _: &mut eframe::Frame) {
        self.apply_theme(ctx);
        self.save_bounds_if_needed(ctx);
        self.handle_keys(ctx);

        let refresh_secs = (self.settings.refresh_minutes.max(1) as u64) * 60;
        if self.last_tick.elapsed() > Duration::from_secs(30) {
            self.last_tick = Instant::now();
            ctx.request_repaint_after(Duration::from_secs(30));
        }
        if self.last_refresh.elapsed() > Duration::from_secs(refresh_secs) {
            self.reload_feed();
        }
        expire_toast(&mut self.error_toast, 6);
        expire_toast(&mut self.success_toast, 3);

        ctx.send_viewport_cmd(egui::ViewportCommand::WindowLevel(
            if self.settings.always_on_top {
                egui::WindowLevel::AlwaysOnTop
            } else {
                egui::WindowLevel::Normal
            },
        ));

        let accent = self.accent();
        let alpha = ((self.settings.opacity * 255.0) as u8).max(160);
        let fill = if self.settings.dark_theme {
            Color32::from_rgba_unmultiplied(18, 18, 24, alpha)
        } else {
            Color32::from_rgba_unmultiplied(245, 245, 250, alpha)
        };

        egui::CentralPanel::default()
            .frame(
                Frame::none()
                    .fill(fill)
                    .rounding(egui::Rounding::same(14.0))
                    .stroke(Stroke::new(1.0_f32, accent.gamma_multiply(0.5)))
                    .inner_margin(Margin::same(12.0)),
            )
            .show(ctx, |ui| {
                self.draw_header(ui, ctx, accent);
                ui.add_space(2.0);
                ui.label(RichText::new(&self.status).size(11.0).color(Color32::GRAY));
                if !self.show_settings {
                    ui.add_space(4.0);
                    ui.horizontal(|ui| {
                        ui.label(RichText::new("🔍").size(13.0));
                        ui.add(
                            egui::TextEdit::singleline(&mut self.search)
                                .hint_text("搜索标题 / 平台…")
                                .desired_width(ui.available_width() - 100.0),
                        );
                        ui.checkbox(&mut self.settings.only_today, "仅今日");
                    });
                }
                ui.separator();
                if self.show_settings {
                    self.draw_settings(ui);
                } else {
                    let items = self.filtered_items();
                    let total = items.len();
                    egui::ScrollArea::vertical()
                        .auto_shrink([false, false])
                        .show(ui, |ui| {
                            for item in &items {
                                self.draw_card(ui, ctx, item, accent);
                                ui.add_space(6.0);
                            }
                            if total == 0 {
                                ui.centered_and_justified(|ui| {
                                    ui.label(if self.feed.items.is_empty() {
                                        "暂无番剧数据"
                                    } else {
                                        "没有匹配的条目"
                                    });
                                });
                            }
                        });
                    if !self.search.is_empty() || self.settings.only_today {
                        ui.label(
                            RichText::new(format!("显示 {total} / {}", self.feed.items.len()))
                                .size(10.0)
                                .color(Color32::GRAY),
                        );
                    }
                }
                if let Some((msg, _)) = &self.error_toast {
                    ui.add_space(4.0);
                    ui.colored_label(Color32::from_rgb(255, 120, 120), msg);
                }
                if let Some((msg, _)) = &self.success_toast {
                    ui.add_space(4.0);
                    ui.colored_label(Color32::from_rgb(110, 231, 183), msg);
                }
            });
    }

    fn on_exit(&mut self, _: Option<&eframe::glow::Context>) {
        let _ = self.settings.save();
    }
}

include!("app_draw.inc");
