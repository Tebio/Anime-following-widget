//! 用真实抓取的 agedm 首页（2026-07-26）做解析回归测试。

use anime_widget::schedule::{parse_schedule, WeekSchedule};

fn fixture() -> WeekSchedule {
    let html = include_str!("fixtures/agedm_home.html");
    parse_schedule(html, "2026-07-26 16:40".into())
}

#[test]
fn parses_all_seven_days() {
    let s = fixture();
    assert_eq!(s.days.len(), 7);
    for (i, d) in s.days.iter().enumerate() {
        assert_eq!(d.weekday, i);
    }
    // 每天都应该有条目
    for d in &s.days {
        assert!(!d.entries.is_empty(), "weekday {} 为空", d.weekday);
    }
}

#[test]
fn monday_first_entry() {
    let s = fixture();
    let mon = &s.days[0];
    assert_eq!(
        mon.entries[0].title,
        "说出你们先走我断后的十年后 我成为了传说"
    );
    assert_eq!(mon.entries[0].detail_id, "20260215");
    assert_eq!(mon.entries[0].label, "第04集");
    assert_eq!(mon.entries[0].time, None);
}

#[test]
fn sunday_entries_with_time_and_new_badge() {
    let s = fixture();
    let sun = &s.days[6];
    let first = &sun.entries[0];
    assert_eq!(first.title, "Grow Up Show ～向日葵马戏团～");
    assert!(first.is_new);
    assert_eq!(first.time.as_deref(), Some("23:00"));
    assert_eq!(first.label, "第04集");
}

#[test]
fn ended_detection() {
    let s = fixture();
    let sun = &s.days[6];
    let ended: Vec<_> = sun.entries.iter().filter(|e| e.is_end).collect();
    assert!(
        ended.len() >= 2,
        "周日应至少 2 部完结，实际 {}",
        ended.len()
    );
    assert!(sun
        .entries
        .iter()
        .any(|e| e.title.contains("人造人009") && e.is_end));
}

#[test]
fn every_entry_has_detail_id() {
    let s = fixture();
    for d in &s.days {
        for e in &d.entries {
            assert!(!e.detail_id.is_empty(), "{} 缺 detail_id", e.title);
            assert!(e.detail_id.chars().all(|c| c.is_ascii_digit()));
        }
    }
}

#[test]
fn search_url_format() {
    let s = fixture();
    let e = &s.days[6].entries[0];
    assert!(e
        .search_url()
        .starts_with("https://www.agedm.io/search?query="));
    assert!(!e.search_url().contains(' '));
}

#[test]
fn base_recorded_and_mirror_urls() {
    let s = fixture();
    assert_eq!(s.base, "https://www.agedm.io");
    let e = &s.days[6].entries[0];
    assert_eq!(
        e.detail_url(),
        format!("https://www.agedm.io/detail/{}", e.detail_id)
    );
    // 镜像 base 替换生效
    assert!(e
        .detail_url_with("https://www.age.tv")
        .starts_with("https://www.age.tv/detail/"));
    assert!(e
        .search_url_with("https://www.age.tv")
        .starts_with("https://www.age.tv/search?query="));
}
