impl AnimeWidgetApp {
    fn draw_header(&mut self, ui: &mut egui::Ui, ctx: &egui::Context, accent: Color32) {
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
                if ui
                    .add(
                        egui::Button::new(RichText::new("×").size(16.0).color(Color32::WHITE))
                            .fill(Color32::from_rgb(220, 50, 50))
                            .min_size(Vec2::new(28.0, 28.0)),
                    )
                    .clicked()
                {
                    let _ = self.settings.save();
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
                if ui.button(RichText::new("⚙").size(14.0)).on_hover_text("设置 (Ctrl+,)").clicked()
                {
                    self.show_settings = !self.show_settings;
                    self.feed_source_edit = self.settings.feed_source.clone();
                }
                if ui
                    .button(RichText::new(if self.settings.dark_theme { "◐" } else { "◑" }).size(14.0))
                    .on_hover_text("主题 (Ctrl+T)")
                    .clicked()
                {
                    self.settings.dark_theme = !self.settings.dark_theme;
                    let _ = self.settings.save();
                }
                if ui
                    .button(RichText::new(if self.loading { "…" } else { "↻" }).size(14.0))
                    .on_hover_text("刷新 (F5)")
                    .clicked()
                {
                    self.reload_feed();
                }
            });
        });
    }

    fn draw_card(&mut self, ui: &mut egui::Ui, ctx: &egui::Context, item: &FeedItem, accent: Color32) {
        let today = item.is_today_update();
        let bg = if self.settings.dark_theme {
            Color32::from_rgb(32, 32, 40)
        } else {
            Color32::from_rgb(255, 255, 255)
        };
        let response = Frame::none()
            .fill(bg)
            .rounding(egui::Rounding::same(10.0))
            .stroke(Stroke::new(
                1.5_f32,
                if today {
                    Color32::from_rgb(239, 68, 68)
                } else {
                    accent.gamma_multiply(0.35)
                },
            ))
            .inner_margin(Margin::symmetric(12.0, 10.0))
            .show(ui, |ui| {
                ui.set_min_width(ui.available_width());
                ui.horizontal(|ui| {
                    let (rect, _) = ui.allocate_exact_size(Vec2::new(4.0, 64.0), Sense::hover());
                    ui.painter().rect_filled(
                        rect,
                        2.0,
                        if today {
                            Color32::from_rgb(239, 68, 68)
                        } else {
                            accent
                        },
                    );
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
                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                ui.label(RichText::new("观看 ▶").size(12.0).color(accent).strong());
                            });
                        });
                        let ep = if item.latest_episode > 0 {
                            format!("最新: 第{}集", item.latest_episode)
                        } else if !item.episode.is_empty() {
                            format!("最新: {}", item.episode)
                        } else {
                            "最新: —".into()
                        };
                        ui.label(RichText::new(ep).size(12.0));
                        let upd = if today {
                            "🔴 今日更新！".into()
                        } else if item.update_weekday.is_some() {
                            format!("每周{}更新", item.weekday_text())
                        } else {
                            "更新时间未知".into()
                        };
                        ui.label(RichText::new(upd).size(11.0).color(if today {
                            Color32::from_rgb(248, 113, 113)
                        } else {
                            Color32::GRAY
                        }));
                        ui.horizontal(|ui| {
                            if !item.time.is_empty() {
                                ui.label(RichText::new(&item.time).size(11.0).color(Color32::GRAY));
                            }
                            if !item.platform.is_empty() {
                                ui.label(RichText::new(&item.platform).size(11.0).color(Color32::GRAY));
                            }
                            if !item.notes.is_empty() {
                                ui.label(
                                    RichText::new(format!("· {}", item.notes))
                                        .size(10.0)
                                        .color(Color32::from_gray(140)),
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
            ui.painter().rect_stroke(
                response.rect,
                10.0,
                Stroke::new(1.5_f32, accent.gamma_multiply(0.7)),
            );
        }
        if response.clicked() {
            self.open_item(item);
        }
        response.context_menu(|ui| {
            if ui.button("打开播放页").clicked() {
                self.open_item(item);
                ui.close_menu();
            }
            if ui.button("复制链接").clicked() {
                self.copy_item_url(ctx, item);
                ui.close_menu();
            }
            if !item.anime_id.is_empty() {
                ui.label(
                    RichText::new(format!("ID: {}", item.anime_id))
                        .size(10.0)
                        .color(Color32::GRAY),
                );
            }
        });
    }

    fn draw_settings(&mut self, ui: &mut egui::Ui) {
        ui.heading("设置");
        ui.add_space(6.0);
        ui.label("数据源（HTTP / 本地 JSON，留空 = 内置示例）");
        ui.add(
            egui::TextEdit::singleline(&mut self.feed_source_edit)
                .desired_width(f32::INFINITY)
                .hint_text("https://... 或本地路径"),
        );
        ui.add_space(6.0);
        ui.checkbox(&mut self.settings.always_on_top, "始终置顶");
        ui.checkbox(&mut self.settings.dark_theme, "深色主题");
        ui.checkbox(&mut self.settings.only_today, "默认仅显示今日更新");
        ui.horizontal(|ui| {
            ui.label("排序");
            egui::ComboBox::from_id_salt("sort_mode")
                .selected_text(
                    SORT_LABELS
                        .get(self.settings.sort_mode as usize)
                        .copied()
                        .unwrap_or("原始顺序"),
                )
                .show_ui(ui, |ui| {
                    for (i, label) in SORT_LABELS.iter().enumerate() {
                        ui.selectable_value(&mut self.settings.sort_mode, i as u8, *label);
                    }
                });
        });
        ui.horizontal(|ui| {
            ui.label("自动刷新（分钟）");
            ui.add(egui::DragValue::new(&mut self.settings.refresh_minutes).range(1..=180));
        });
        ui.horizontal(|ui| {
            ui.label("强调色");
            for (i, c) in ACCENTS.iter().enumerate() {
                let selected = self.settings.accent_index as usize == i;
                if ui
                    .add(
                        egui::Button::new(if selected { "●" } else { " " })
                            .fill(*c)
                            .min_size(Vec2::splat(24.0)),
                    )
                    .clicked()
                {
                    self.settings.accent_index = i as u8;
                }
            }
        });
        ui.add(egui::Slider::new(&mut self.settings.opacity, 0.5..=1.0).text("不透明度"));
        ui.add_space(10.0);
        ui.horizontal(|ui| {
            if ui.button(RichText::new("保存并刷新").strong()).clicked() {
                self.settings.feed_source = self.feed_source_edit.trim().to_string();
                let _ = self.settings.save();
                self.reload_feed();
                self.show_settings = false;
                self.success_toast = Some(("设置已保存".into(), Instant::now()));
            }
            if ui.button("取消").clicked() {
                self.show_settings = false;
            }
            if ui.button("恢复默认").clicked() {
                self.settings = WidgetSettings::default();
                self.feed_source_edit.clear();
            }
        });
        ui.add_space(10.0);
        ui.label(RichText::new("快捷键").strong().size(12.0));
        ui.label(
            RichText::new("F5/Ctrl+R 刷新 · Ctrl+, 设置 · Ctrl+T 主题 · Esc 关闭 · 右键复制链接")
                .size(11.0)
                .color(Color32::GRAY),
        );
        ui.add_space(8.0);
        ui.label(
            RichText::new(format!("配置: {}", crate::settings::settings_path().display()))
                .size(10.0)
                .color(Color32::GRAY),
        );
    }
}
