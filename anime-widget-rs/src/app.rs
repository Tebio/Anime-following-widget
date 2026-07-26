use crate::agedm;
use crate::feed;
use crate::models::{FeedData, FeedItem};
use crate::settings::WidgetSettings;
use eframe::egui::{self, Align, Color32, Frame, Layout, Margin, RichText, Sense, Stroke, Vec2};
use std::time::{Duration, Instant};

const ACCENTS: [Color32; 5] = [
    Color32::from_rgb(167, 139, 250), // 紫
    Color32::from_rgb(34, 211, 238),  // 青
    Color32::from_rgb(74, 222, 128),  // 绿
    Color32::from_rgb(244, 114, 182), // 粉
    Color32::from_rgb(251, 146, 60),  // 橙
];

pub struct AnimeWidgetApp {
    settings: WidgetSettings,
    feed: FeedData,
    status: String,
    show_settings: bool,
    feed_source_edit: String,
    last_refresh: Instant,
    last_tick: Instant,
    error_toast: Option<(String, Instant)>,
}

impl AnimeWidgetApp {
    pub fn new(cc: &eframe::CreationContext<'_>) -> Self {
        let settings = WidgetSettings::load();
        let mut style = (*cc.egui_ctx.style()).clone();
        style.spacing.item_spacing = Vec2::new(8.0, 6.0);
        cc.egui_ctx.set_style(style);

        let mut app = Self {
            feed_source_edit: settings.feed_source.clone(),
            settings,
            feed: FeedData::default(),
            status: String::new(),
            show_settings: false,
            last_refresh: Instant::now() - Duration::from_secs(3600),
            last_tick: Instant::now(),
            error_toast: None,
        };
        app.reload_feed();
        app
    }

    fn accent(&self) -> Color32 {
        ACCENTS[(self.settings.accent_index as usize) % ACCENTS.len()]
    }

    fn reload_feed(&mut self) {
        match feed::load_feed(&self.settings.feed_source) {
            Ok(data) => {
                self.status = format!("已加载 {} 部 · {}", data.items.len(), data.title);
                self.feed = data;
                self.last_refresh = Instant::now();
            }
            Err(e) => {
                self.status = format!("加载失败: {e}");
                if let Ok(data) = feed::load_feed("") {
                    self.feed = data;
                }
                self.error_toast = Some((format!("{e}"), Instant::now()));
            }
        }
    }

    fn open_item(&mut self, item: &FeedItem) {
        let ep = item.resolved_episode();
        match agedm::resolve_watch_url(&item.anime_id, item.season_number, ep, &item.watch_url) {
            Ok(url) => {
                if let Err(e) = open::that(&url) {
                    self.error_toast =
                        Some((format!("无法打开链接: {e}\n{url}"), Instant::now()));
                }
            }
            Err(e) => {
                self.error_toast = Some((e, Instant::now()));
            }
        }
    }

    fn apply_theme(&self, ctx: &egui::Context) {
        let mut visuals = if self.settings.dark_theme {
            egui::Visuals::dark()
        } else {
            egui::Visuals::light()
        };
        visuals.window_rounding = egui::Rounding::same(12.0);
        visuals.panel_fill = if self.settings.dark_theme {
            Color32::from_rgba_unmultiplied(22, 22, 28, 230)
        } else {
            Color32::from_rgba_unmultiplied(248, 248, 252, 240)
        };
        ctx.set_visuals(visuals);
    }
}

impl eframe::App for AnimeWidgetApp {
    fn clear_color(&self, _visuals: &egui::Visuals) -> [f32; 4] {
        [0.0, 0.0, 0.0, 0.0]
    }

    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.apply_theme(ctx);

        if self.last_tick.elapsed() > Duration::from_secs(30) {
            self.last_tick = Instant::now();
            ctx.request_repaint_after(Duration::from_secs(30));
        }
        if self.last_refresh.elapsed() > Duration::from_secs(15 * 60) {
            self.reload_feed();
        }

        if let Some((_, t)) = &self.error_toast {
            if t.elapsed() > Duration::from_secs(5) {
                self.error_toast = None;
            }
        }

        ctx.send_viewport_cmd(egui::ViewportCommand::WindowLevel(
            if self.settings.always_on_top {
                egui::WindowLevel::AlwaysOnTop
            } else {
                egui::WindowLevel::Normal
            },
        ));

        let accent = self.accent();
        let alpha = ((self.settings.opacity * 255.0) as u8).max(180);

        egui::CentralPanel::default()
            .frame(
                Frame::none()
                    .fill(if self.settings.dark_theme {
                        Color32::from_rgba_unmultiplied(18, 18, 24, alpha)
                    } else {
                        Color32::from_rgba_unmultiplied(245, 245, 250, alpha)
                    })
                    .rounding(egui::Rounding::same(14.0))
                    .stroke(Stroke::new(1.0, accent.gamma_multiply(0.5)))
                    .inner_margin(Margin::same(12.0)),
            )
            .show(ctx, |ui| {
                ui.horizontal(|ui| {
                    let title = ui.add(
                        egui::Label::new(
                            RichText::new(format!("📺 {}", self.feed.title))
                                .strong()
                                .size(16.0)
                                .color(accent),
                        )
                        .sense(Sense::click_and_drag()),
                    );
                    if title.dragged() || title.is_pointer_button_down_on() {
                        ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
                    }

                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        let close = ui.add(
                            egui::Button::new(RichText::new("×").size(16.0).color(Color32::WHITE))
                                .fill(Color32::from_rgb(220, 50, 50))
                                .min_size(Vec2::new(28.0, 28.0)),
                        );
                        if close.clicked() {
                            let _ = self.settings.save();
                            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                        }

                        if ui
                            .button(RichText::new("⚙").size(14.0))
                            .on_hover_text("设置")
                            .clicked()
                        {
                            self.show_settings = !self.show_settings;
                            self.feed_source_edit = self.settings.feed_source.clone();
                        }

                        if ui
                            .button(RichText::new("↻").size(14.0))
                            .on_hover_text("刷新")
                            .clicked()
                        {
                            self.reload_feed();
                        }
                    });
                });

                ui.add_space(4.0);
                ui.label(RichText::new(&self.status).size(11.0).color(Color32::GRAY));
                ui.separator();

                if self.show_settings {
                    self.draw_settings(ui);
                } else {
                    egui::ScrollArea::vertical()
                        .auto_shrink([false, false])
                        .show(ui, |ui| {
                            let items = self.feed.items.clone();
                            for item in &items {
                                self.draw_card(ui, item, accent);
                                ui.add_space(6.0);
                            }
                            if items.is_empty() {
                                ui.centered_and_justified(|ui| {
                                    ui.label("暂无番剧数据");
                                });
                            }
                        });
                }

                if let Some((msg, _)) = &self.error_toast {
                    ui.add_space(6.0);
                    ui.colored_label(Color32::from_rgb(255, 120, 120), msg);
                }
            });
    }

    fn on_exit(&mut self, _gl: Option<&eframe::glow::Context>) {
        let _ = self.settings.save();
    }
}

impl AnimeWidgetApp {
    fn draw_card(&mut self, ui: &mut egui::Ui, item: &FeedItem, accent: Color32) {
        let today = item.is_today_update();
        let bg = if self.settings.dark_theme {
            Color32::from_rgb(32, 32, 40)
        } else {
            Color32::from_rgb(255, 255, 255)
        };

        let frame = Frame::none()
            .fill(bg)
            .rounding(egui::Rounding::same(10.0))
            .stroke(Stroke::new(
                1.5,
                if today {
                    Color32::from_rgb(239, 68, 68)
                } else {
                    accent.gamma_multiply(0.4)
                },
            ))
            .inner_margin(Margin::symmetric(12.0, 10.0));

        let response = frame
            .show(ui, |ui| {
                ui.set_min_width(ui.available_width());
                ui.horizontal(|ui| {
                    let (rect, _) =
                        ui.allocate_exact_size(Vec2::new(4.0, 56.0), Sense::hover());
                    ui.painter().rect_filled(rect, 2.0, accent);

                    ui.vertical(|ui| {
                        ui.horizontal(|ui| {
                            ui.label(RichText::new(&item.title).strong().size(14.0));
                            if !item.badge.is_empty() {
                                ui.label(
                                    RichText::new(format!("[{}]", item.badge))
                                        .size(11.0)
                                        .color(accent),
                                );
                            }
                        });

                        let ep_text = if item.latest_episode > 0 {
                            format!("最新: 第{}集", item.latest_episode)
                        } else if !item.episode.is_empty() {
                            format!("最新: {}", item.episode)
                        } else {
                            "最新: —".into()
                        };
                        ui.label(RichText::new(ep_text).size(12.0));

                        let update_text = if today {
                            "🔴 今日更新！".to_string()
                        } else if item.update_weekday.is_some() {
                            format!("每周{}更新", item.weekday_text())
                        } else {
                            "更新时间未知".into()
                        };
                        ui.label(
                            RichText::new(update_text).size(11.0).color(if today {
                                Color32::from_rgb(248, 113, 113)
                            } else {
                                Color32::GRAY
                            }),
                        );

                        ui.horizontal(|ui| {
                            if !item.time.is_empty() {
                                ui.label(
                                    RichText::new(&item.time).size(11.0).color(Color32::GRAY),
                                );
                            }
                            if !item.platform.is_empty() {
                                ui.label(
                                    RichText::new(&item.platform)
                                        .size(11.0)
                                        .color(Color32::GRAY),
                                );
                            }
                        });
                    });
                });
            })
            .response
            .interact(Sense::click());

        if response.hovered() {
            ui.ctx().set_cursor_icon(egui::CursorIcon::PointingHand);
        }
        if response.clicked() {
            self.open_item(item);
        }
    }

    fn draw_settings(&mut self, ui: &mut egui::Ui) {
        ui.heading("设置");
        ui.add_space(6.0);

        ui.label("数据源（HTTP URL 或本地 JSON 路径，留空用内置示例）");
        ui.text_edit_singleline(&mut self.feed_source_edit);

        ui.checkbox(&mut self.settings.always_on_top, "始终置顶");
        ui.checkbox(&mut self.settings.dark_theme, "深色主题");

        ui.horizontal(|ui| {
            ui.label("强调色");
            for (i, c) in ACCENTS.iter().enumerate() {
                let selected = self.settings.accent_index as usize == i;
                let btn = ui.add(
                    egui::Button::new(if selected { "●" } else { " " })
                        .fill(*c)
                        .min_size(Vec2::splat(24.0)),
                );
                if btn.clicked() {
                    self.settings.accent_index = i as u8;
                }
            }
        });

        ui.add(egui::Slider::new(&mut self.settings.opacity, 0.5..=1.0).text("不透明度"));

        ui.add_space(8.0);
        ui.horizontal(|ui| {
            if ui.button("保存并刷新").clicked() {
                self.settings.feed_source = self.feed_source_edit.trim().to_string();
                let _ = self.settings.save();
                self.reload_feed();
                self.show_settings = false;
            }
            if ui.button("取消").clicked() {
                self.show_settings = false;
            }
        });

        ui.add_space(8.0);
        ui.label(
            RichText::new(format!(
                "配置: {}",
                crate::settings::settings_path().display()
            ))
            .size(10.0)
            .color(Color32::GRAY),
        );
    }
}
