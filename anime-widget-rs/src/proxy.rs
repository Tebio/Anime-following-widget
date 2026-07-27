//! 系统代理检测：ureq 默认不走 Windows 系统代理，
//! 国内访问 agedm 常需代理（OpenClash 等），这里读注册表 IE 代理设置 + env 兜底。

/// 返回可用的代理 URL（如 "http://127.0.0.1:7890"），无代理返回 None。
pub fn detect_proxy() -> Option<String> {
    // Windows：注册表系统代理优先
    #[cfg(windows)]
    if let Some(p) = registry_proxy() {
        return Some(p);
    }
    // env 兜底（Linux/macOS/手动设置）
    for var in [
        "HTTPS_PROXY",
        "https_proxy",
        "HTTP_PROXY",
        "http_proxy",
        "ALL_PROXY",
        "all_proxy",
    ] {
        if let Ok(v) = std::env::var(var) {
            let v = v.trim().to_string();
            if !v.is_empty() {
                return Some(with_scheme(&v));
            }
        }
    }
    None
}

/// 解析注册表 ProxyServer 值：
///   "127.0.0.1:7890"                              -> http://127.0.0.1:7890
///   "http=1.2.3.4:8080;https=1.2.3.4:8080"        -> 取 https
///   "socks=127.0.0.1:1080"                        -> socks5://127.0.0.1:1080
pub fn parse_proxy_server(raw: &str) -> Option<String> {
    let raw = raw.trim();
    if raw.is_empty() {
        return None;
    }
    if raw.contains('=') {
        let mut best: Option<(u8, String)> = None;
        for part in raw.split(';') {
            let Some((k, v)) = part.split_once('=') else {
                continue;
            };
            let k = k.trim().to_ascii_lowercase();
            let v = v.trim();
            if v.is_empty() {
                continue;
            }
            // 优先级 https > http > socks > 其它
            let rank = match k.as_str() {
                "https" => 0,
                "http" => 1,
                "socks" => 2,
                _ => 3,
            };
            if best.as_ref().map_or(true, |(r, _)| rank < *r) {
                let url = if k == "socks" {
                    format!("socks5://{v}")
                } else {
                    with_scheme(v)
                };
                best = Some((rank, url));
            }
        }
        best.map(|(_, url)| url)
    } else {
        Some(with_scheme(raw))
    }
}

fn with_scheme(v: &str) -> String {
    if v.contains("://") {
        v.to_string()
    } else {
        format!("http://{v}")
    }
}

#[cfg(windows)]
fn registry_proxy() -> Option<String> {
    use windows::core::{w, HSTRING, PCWSTR};
    use windows::Win32::System::Registry::*;

    let subkey = HSTRING::from("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings");
    let subkey = PCWSTR::from_raw(subkey.as_ptr());

    unsafe {
        // ProxyEnable (DWORD)
        let mut enable: u32 = 0;
        let mut cb = std::mem::size_of::<u32>() as u32;
        let err = RegGetValueW(
            HKEY_CURRENT_USER,
            subkey,
            w!("ProxyEnable"),
            RRF_RT_REG_DWORD,
            None,
            Some(&mut enable as *mut u32 as *mut _),
            Some(&mut cb),
        );
        if err.0 != 0 || enable == 0 {
            return None;
        }

        // ProxyServer (SZ)
        let mut buf = [0u16; 512];
        let mut cb = (buf.len() * 2) as u32;
        let err = RegGetValueW(
            HKEY_CURRENT_USER,
            subkey,
            w!("ProxyServer"),
            RRF_RT_REG_SZ,
            None,
            Some(buf.as_mut_ptr() as *mut _),
            Some(&mut cb),
        );
        if err.0 != 0 || cb < 4 {
            return None;
        }
        let len = cb as usize / 2 - 1; // 去掉末尾 NUL
        let raw = String::from_utf16_lossy(&buf[..len]);
        parse_proxy_server(&raw)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_simple_host_port() {
        assert_eq!(
            parse_proxy_server("127.0.0.1:7890"),
            Some("http://127.0.0.1:7890".into())
        );
    }

    #[test]
    fn parse_per_protocol_prefers_https() {
        assert_eq!(
            parse_proxy_server("http=1.2.3.4:8080;https=5.6.7.8:8443"),
            Some("http://5.6.7.8:8443".into())
        );
    }

    #[test]
    fn parse_socks() {
        assert_eq!(
            parse_proxy_server("socks=127.0.0.1:1080"),
            Some("socks5://127.0.0.1:1080".into())
        );
    }

    #[test]
    fn parse_empty_and_garbage() {
        assert_eq!(parse_proxy_server(""), None);
        assert_eq!(parse_proxy_server("   "), None);
        assert_eq!(parse_proxy_server("http=;https="), None);
    }

    #[test]
    fn keeps_existing_scheme() {
        assert_eq!(
            parse_proxy_server("http://127.0.0.1:7890"),
            Some("http://127.0.0.1:7890".into())
        );
    }
}
