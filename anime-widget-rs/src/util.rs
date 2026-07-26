use std::time::{Duration, Instant};

pub fn expire_toast(t: &mut Option<(String, Instant)>, secs: u64) {
    if let Some((_, at)) = t {
        if at.elapsed() > Duration::from_secs(secs) {
            *t = None;
        }
    }
}

pub fn truncate_middle(s: &str, max: usize) -> String {
    let chars: Vec<char> = s.chars().collect();
    if chars.len() <= max {
        return s.to_string();
    }
    let keep = max.saturating_sub(1) / 2;
    let left: String = chars.iter().take(keep).collect();
    let right: String = chars
        .iter()
        .rev()
        .take(keep)
        .collect::<String>()
        .chars()
        .rev()
        .collect();
    format!("{left}…{right}")
}
