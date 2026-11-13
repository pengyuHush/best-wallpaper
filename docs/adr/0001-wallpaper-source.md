# 壁纸源：wallhaven 为主，Bing 每日图为第二层兜底

个人使用的壁纸轮播工具需要能按主题搜索的免费图源，且不应要求用户注册。最初的选择是「官方 API 优先」，但 Pexels 的 API 文档明文禁止 *"making Pexels content available as a wallpaper app"*，Unsplash 的条款页因反爬无法核实且要求强制署名与 hotlink。wallhaven 的 API 文档面向第三方应用（"as you create apps that take advantage of our API"），免 API key，限速 45 次/分钟，返回原图与完好的分辨率元数据，因此选它为主源。

**Considered Options**: Pexels（条款明确禁止壁纸应用）、Unsplash（条款不可核实、需 key 与署名）、Pixabay（需 key，条款对壁纸应用的边界模糊）、Bing 每日图（免 key 但只有每日一图，无法按主题搜索）。

**Consequences**: wallhaven 在本机被 DNS 污染——直连 30 秒超时，实测必须经系统代理（约 1.5 秒）。代理不可用时按兜底链降级到 Bing 每日图（直连约 100ms）再到本地历史库。图片版权属原作者，仅限于个人桌面使用，不再分发。
