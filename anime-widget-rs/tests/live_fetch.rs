//! 真实联网测试（默认忽略）：cargo test -- --ignored
//! 端到端验证两个镜像 + 代理检测 + 解析。

use anime_widget::schedule::{fetch_schedule, MIRRORS};

#[test]
#[ignore = "需要联网，手动跑"]
fn live_fetch_mirrors() {
    let sched = fetch_schedule().expect("所有镜像都失败");
    println!("成功镜像: {}", sched.base);
    assert!(MIRRORS.iter().any(|m| sched.base.starts_with(m)));
    assert_eq!(sched.days.len(), 7);
    let total: usize = sched.days.iter().map(|d| d.entries.len()).sum();
    println!("本周共 {total} 部");
    for d in &sched.days {
        println!("  周{}: {} 部", ["一", "二", "三", "四", "五", "六", "日"][d.weekday], d.entries.len());
    }
    assert!(total > 50, "周表条目过少，可能解析错页面");
    // 至少一天有时刻信息
    assert!(sched
        .days
        .iter()
        .flat_map(|d| &d.entries)
        .any(|e| e.time.is_some()));
}
