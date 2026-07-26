//! 加载系统中文字体，解决 egui 默认字体无 CJK 字形导致乱码的问题。

use eframe::egui;

/// 在 Windows 上按优先级尝试系统字体（优先 TTF，其次 TTC）。
fn candidate_fonts() -> Vec<std::path::PathBuf> {
    let mut paths = Vec::new();

    // 常见 Windows 字体目录
    let windir = std::env::var("WINDIR").unwrap_or_else(|_| r"C:\Windows".into());
    let fonts_dir = std::path::PathBuf::from(&windir).join("Fonts");

    // 顺序：简体优先，再繁体 / 通用
    for name in [
        "msyh.ttc",     // 微软雅黑（最常见）
        "msyh.ttf",
        "msyhl.ttc",    // 微软雅黑 Light
        "simhei.ttf",   // 黑体（纯 TTF，兼容性好）
        "simsun.ttc",   // 宋体
        "simkai.ttf",   // 楷体
        "msjh.ttc",     // 微软正黑体（繁体）
        "msjh.ttf",
        "Deng.ttf",     // 等线
        "Dengb.ttf",
        "NotoSansSC-Regular.otf",
        "NotoSansCJKsc-Regular.otf",
        "SourceHanSansSC-Regular.otf",
    ] {
        paths.push(fonts_dir.join(name));
    }

    // 用户可能装在 LocalAppData 的字体
    if let Ok(local) = std::env::var("LOCALAPPDATA") {
        let user_fonts = std::path::PathBuf::from(local).join("Microsoft\\Windows\\Fonts");
        for name in ["msyh.ttc", "simhei.ttf", "NotoSansSC-Regular.otf"] {
            paths.push(user_fonts.join(name));
        }
    }

    paths
}

/// 向 egui 注入中文字体：作为 Proportional / Monospace 的首选。
pub fn setup_cjk_fonts(ctx: &egui::Context) {
    let mut font_bytes: Option<(Vec<u8>, u32, String)> = None;

    for path in candidate_fonts() {
        if !path.exists() {
            continue;
        }
        match std::fs::read(&path) {
            Ok(data) if data.len() > 1000 => {
                let name = path
                    .file_name()
                    .and_then(|s| s.to_str())
                    .unwrap_or("cjk")
                    .to_string();
                // TTC 集合通常 index=0 就是常规字重
                font_bytes = Some((data, 0, name));
                break;
            }
            _ => continue,
        }
    }

    let Some((data, index, name)) = font_bytes else {
        // 找不到系统中文字体：保持默认（英文环境会仍是方框/乱码提示）
        eprintln!("AnimeWidget: 未找到系统中文字体，界面中文可能显示异常");
        return;
    };

    let mut fonts = egui::FontDefinitions::default();

    let mut font_data = egui::FontData::from_owned(data);
    font_data.index = index;
    fonts.font_data.insert(name.clone(), font_data);

    // 放到最前：优先用中文字体绘制，缺字再回落到默认
    if let Some(family) = fonts.families.get_mut(&egui::FontFamily::Proportional) {
        family.insert(0, name.clone());
    }
    if let Some(family) = fonts.families.get_mut(&egui::FontFamily::Monospace) {
        family.insert(0, name.clone());
    }

    ctx.set_fonts(fonts);
    eprintln!("AnimeWidget: 已加载中文字体 {name}");
}
