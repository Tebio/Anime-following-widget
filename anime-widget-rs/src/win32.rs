//! Windows 桌面层：把窗口挂到桌面壁纸层（WorkerW）或置底，实现
//! 「只在桌面可见，打开窗口就被盖住」。
//! 另提供：鼠标穿透、整体透明度、窗口移动/取位置、屏幕尺寸。
//! 非 Windows 平台全部降级为空操作（方便 Linux 下跑测试）。

use serde::{Deserialize, Serialize};

/// 桌面嵌入方式
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum EmbedMode {
    /// 挂到壁纸层 WorkerW：Win+D 不消失（部分系统上点击可能不命中）
    WorkerW,
    /// 普通窗口压到 Z 序最底：点击兼容性最好，但 Win+D 会一起隐藏
    BottomPin,
}

impl Default for EmbedMode {
    fn default() -> Self {
        EmbedMode::WorkerW
    }
}

#[cfg(windows)]
mod imp {
    use super::EmbedMode;
    use windows::core::{w, BOOL, PCWSTR};
    use windows::Win32::Foundation::{HWND, LPARAM, WPARAM};
    use windows::Win32::UI::WindowsAndMessaging::*;

    pub struct DesktopLayer {
        hwnd: HWND,
        mode: EmbedMode,
    }

    impl DesktopLayer {
        /// 由 eframe 的 raw window handle 创建，并尝试挂到桌面层。
        pub fn attach(hwnd_isize: isize, want: EmbedMode) -> Self {
            let hwnd = HWND(hwnd_isize as _);
            let mut layer = DesktopLayer {
                hwnd,
                mode: EmbedMode::BottomPin,
            };
            unsafe {
                // 不参与 Alt-Tab / 任务栏，不抢焦点
                layer.add_ex_style(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            }
            layer.set_mode(want);
            layer
        }

        pub fn mode(&self) -> EmbedMode {
            self.mode
        }

        /// 运行时切换桌面嵌入方式
        pub fn set_mode(&mut self, mode: EmbedMode) {
            if self.mode == mode {
                return;
            }
            unsafe {
                match mode {
                    EmbedMode::WorkerW => {
                        if let Some(workerw) = find_workerw() {
                            let style = GetWindowLongPtrW(self.hwnd, GWL_STYLE);
                            SetWindowLongPtrW(
                                self.hwnd,
                                GWL_STYLE,
                                (style & !(WS_POPUP.0 as isize)) | (WS_CHILD.0 as isize),
                            );
                            if SetParent(self.hwnd, Some(workerw)).is_ok() {
                                self.mode = EmbedMode::WorkerW;
                                self.refresh_frame();
                                return;
                            }
                        }
                        // 找不到壁纸层就退回置底
                        self.force_bottom_pin();
                    }
                    EmbedMode::BottomPin => self.force_bottom_pin(),
                }
            }
        }

        unsafe fn force_bottom_pin(&mut self) {
            let style = GetWindowLongPtrW(self.hwnd, GWL_STYLE);
            SetWindowLongPtrW(
                self.hwnd,
                GWL_STYLE,
                (style & !(WS_CHILD.0 as isize)) | (WS_POPUP.0 as isize),
            );
            // 解除父子关系，恢复顶层窗口
            let _ = SetParent(self.hwnd, None);
            let _ = SetWindowPos(
                self.hwnd,
                Some(HWND_BOTTOM),
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED,
            );
            self.mode = EmbedMode::BottomPin;
        }

        unsafe fn refresh_frame(&self) {
            let _ = ShowWindow(self.hwnd, SW_SHOW);
            let _ = SetWindowPos(
                self.hwnd,
                None,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED,
            );
        }

        unsafe fn ex_style(&self) -> isize {
            GetWindowLongPtrW(self.hwnd, GWL_EXSTYLE)
        }

        unsafe fn add_ex_style(&self, style: WINDOW_EX_STYLE) {
            let cur = self.ex_style();
            SetWindowLongPtrW(self.hwnd, GWL_EXSTYLE, cur | (style.0 as isize));
        }

        /// 鼠标穿透开关（点击穿过卡片直达桌面）
        pub fn set_click_through(&self, on: bool) {
            unsafe {
                let cur = self.ex_style();
                let next = if on {
                    cur | (WS_EX_TRANSPARENT.0 as isize) | (WS_EX_LAYERED.0 as isize)
                } else {
                    (cur & !(WS_EX_TRANSPARENT.0 as isize)) | (WS_EX_LAYERED.0 as isize)
                };
                SetWindowLongPtrW(self.hwnd, GWL_EXSTYLE, next);
            }
        }

        /// 整体不透明度 0.0~1.0（窗口须为 WS_EX_LAYERED）
        pub fn set_opacity(&self, alpha: f32) {
            let a = (alpha.clamp(0.05, 1.0) * 255.0).round() as u8;
            unsafe {
                self.add_ex_style(WS_EX_LAYERED);
                let _ = SetLayeredWindowAttributes(
                    self.hwnd,
                    windows::Win32::Foundation::COLORREF(0),
                    a,
                    LWA_ALPHA,
                );
            }
        }

        /// 移动窗口（屏幕/桌面坐标，物理像素）
        pub fn set_pos(&self, x: i32, y: i32) {
            unsafe {
                let _ = SetWindowPos(
                    self.hwnd,
                    None,
                    x,
                    y,
                    0,
                    0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE,
                );
            }
        }

        /// 当前窗口位置与尺寸（物理像素）：(x, y, w, h)
        pub fn rect(&self) -> Option<(i32, i32, i32, i32)> {
            let mut rc = windows::Win32::Foundation::RECT::default();
            unsafe { GetWindowRect(self.hwnd, &mut rc).ok()? };
            Some((rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top))
        }

        /// 主屏尺寸（物理像素）
        pub fn screen_size(&self) -> (i32, i32) {
            unsafe { (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)) }
        }
    }

    /// 标准套路：给 Progman 发 0x052C 让它分裂出图标层下的 WorkerW。
    unsafe fn find_workerw() -> Option<HWND> {
        let progman = FindWindowW(w!("Progman"), PCWSTR::null()).ok()?;
        let _ = SendMessageTimeoutW(
            progman,
            0x052C,
            WPARAM(0xD),
            LPARAM(1),
            SMTO_NORMAL,
            1000,
            None,
        );
        let mut result: HWND = HWND::default();
        let _ = EnumWindows(
            Some(enum_find_workerw),
            LPARAM(&mut result as *mut HWND as isize),
        );
        if result.0.is_null() {
            None
        } else {
            Some(result)
        }
    }

    unsafe extern "system" fn enum_find_workerw(top: HWND, lparam: LPARAM) -> BOOL {
        // 含 SHELLDLL_DefView 的顶层窗口之后跟着的 WorkerW 即壁纸层
        if FindWindowExW(Some(top), None, w!("SHELLDLL_DefView"), PCWSTR::null()).is_ok() {
            if let Ok(workerw) = FindWindowExW(None, Some(top), w!("WorkerW"), PCWSTR::null()) {
                let out = &mut *(lparam.0 as *mut HWND);
                *out = workerw;
                return BOOL(0); // 停止枚举
            }
        }
        BOOL(1)
    }
}

#[cfg(not(windows))]
mod imp {
    use super::EmbedMode;

    /// Linux/macOS 下的空实现，只为让 `cargo test` 能跑。
    pub struct DesktopLayer;

    impl DesktopLayer {
        pub fn attach(_hwnd_isize: isize, _want: EmbedMode) -> Self {
            DesktopLayer
        }
        pub fn mode(&self) -> EmbedMode {
            EmbedMode::BottomPin
        }
        pub fn set_mode(&mut self, _mode: EmbedMode) {}
        pub fn set_click_through(&self, _on: bool) {}
        pub fn set_opacity(&self, _alpha: f32) {}
        pub fn set_pos(&self, _x: i32, _y: i32) {}
        pub fn rect(&self) -> Option<(i32, i32, i32, i32)> {
            None
        }
        pub fn screen_size(&self) -> (i32, i32) {
            (1920, 1080)
        }
    }
}

pub use imp::DesktopLayer;
