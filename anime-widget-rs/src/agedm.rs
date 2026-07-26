/// AgeDM 播放链接：https://www.agedm.io/play/{anime_id}/{season}/{episode}

/// 构建 AgeDM 播放页 URL；参数非法时返回 Err
pub fn build_play_url(anime_id: &str, season_number: u32, episode_number: u32) -> Result<String, String> {
    let id = anime_id.trim();
    if id.is_empty() {
        return Err("动漫 ID 不能为空".into());
    }
    if season_number < 1 {
        return Err("季度编号必须大于 0".into());
    }
    if episode_number < 1 {
        return Err("集数必须大于 0".into());
    }
    Ok(format!(
        "https://www.agedm.io/play/{id}/{season_number}/{episode_number}"
    ))
}

/// 优先用 anime_id 生成 AgeDM 链接，否则回退 watch_url
pub fn resolve_watch_url(
    anime_id: &str,
    season_number: u32,
    episode_number: u32,
    watch_url: &str,
) -> Result<String, String> {
    if !anime_id.trim().is_empty() {
        return build_play_url(anime_id, season_number.max(1), episode_number.max(1));
    }
    let url = watch_url.trim();
    if url.is_empty() {
        return Err("无法确定播放链接：缺少 animeId 与 watchUrl".into());
    }
    Ok(url.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn builds_agedm_url() {
        let u = build_play_url("20200283", 1, 184).unwrap();
        assert_eq!(u, "https://www.agedm.io/play/20200283/1/184");
    }
}
