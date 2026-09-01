// Services/SelfTest.cs - 数据层规则自检（--selftest 开关，发版前健康检查）
// 与 Web 版 node 测试脚本同思路：纯函数断言，零 NuGet 外网依赖；
// 用系统临时目录隔离，不碰 data/；跑完 Environment.Exit(code) 不启动主窗。
// 断言清单（对齐 Web 版实现语义）：
//   排序对拍 / 拼音（总数 3755、23 组、已知字）/ CleanUrl 三态 / 去重（link 比 url）
//   滚动归档（500 上限 + id 去重防膨胀）/ round-trip（写→读 字段无损 + camelCase）
//   ResolveExpire / SanitizeInput / IsExpired
using System.IO;
using System.Text.Json;
using ClipboardExe.Models;

namespace ClipboardExe.Services;

public static class SelfTest
{
    private static int _fail;

    public static int Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clipboard-selftest-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        var logFile = Path.Combine(dir, "selftest.log");
        var log = new StringWriter();
        void Line(string s) { Console.WriteLine(s); log.WriteLine(s); }

        Line("=== clipboard data-layer selftest ===");
        try
        {
            var storage = new Storage(dir);

            Check("拼音字库总数 3755", Pinyin.TotalChars == 3755, Line);
            Check("拼音分组 23（I/U/V 无字）", Pinyin.GroupCount == 23, Line);
            Check("strToPy('身份') 返回大写 'SF'（前端比对时才小写）", Pinyin.InitialsOf("身份") == "SF", Line);
            Check("strToPy('你好世界') == 'NHSJ'", Pinyin.InitialsOf("你好世界") == "NHSJ", Line);
            Check("strToPy 非汉字跳过 'abc123' == ''", Pinyin.InitialsOf("abc123") == "", Line);

            // CleanUrl 三态：有追踪参数 / 无参原样 / 畸形原样
            Check("cleanUrl 删 utm_source+fbclid 保留 a",
                CleanUrl.Clean("https://x.com/p?a=1&utm_source=foo&fbclid=bar&b=2") == "https://x.com/p?a=1&b=2", Line);
            Check("cleanUrl 无参原样", CleanUrl.Clean("https://x.com/p?a=1&b=2") == "https://x.com/p?a=1&b=2", Line);
            Check("cleanUrl 畸形原样", CleanUrl.Clean("javascript:alert(1)?utm_source=x") == "javascript:alert(1)?utm_source=x", Line);
            Check("cleanUrl 非 http 原样", CleanUrl.Clean("ftp://x.com?a=1") == "ftp://x.com?a=1", Line);
            Check("cleanUrl 全删去 ?", CleanUrl.Clean("https://x.com/p?utm_source=1") == "https://x.com/p", Line);
            Check("cleanUrl 空串原样", CleanUrl.Clean("") == "", Line);

            // 排序对拍：pinned → copyCount 降序 → updatedAt 降序
            var clips = new List<ClipItem>
            {
                Make("c3", pinned: false, copy: 2, upd: 100),
                Make("c1", pinned: true, copy: 0, upd: 300),
                Make("c4", pinned: false, copy: 2, upd: 50),
                Make("c2", pinned: false, copy: 5, upd: 200),
            };
            var sorted = Storage.SortClips(clips).Select(c => c.Id).ToList();
            Check("排序对拍 [c1,c2,c3,c4]", string.Join(",", sorted) == "c1,c2,c3,c4", Line);

            // 去重：link 比 url、text 比 content、空返回 null
            var dupList = new List<ClipItem>
            {
                Make("d1", type: "link", url: "https://a.com"), Make("d2", type: "text", content: "hello"),
            };
            Check("去重 link 比 url", ClipService.FindDuplicate("https://a.com", dupList)?.Id == "d1", Line);
            Check("去重 text 比 content", ClipService.FindDuplicate("hello", dupList)?.Id == "d2", Line);
            Check("去重 空返回 null", ClipService.FindDuplicate("", dupList) == null, Line);

            // 滚动归档：520 条 → keep 500，溢出按 createdAt 升序入档，id 去重防翻倍
            var big = Enumerable.Range(0, 520).Select(i => Make("b" + i, upd: i, created: i)).ToList();
            var kept = storage.RollToArchive(big);
            var arch1 = storage.LoadArchive();
            Check("归档 keep 500", kept.Count == 500, Line);
            Check("归档入档 20 条", arch1.Count == 20, Line);
            Check("归档按 createdAt 升序（b0 最旧在前）", arch1.First().Id == "b0", Line);
            Check("归档 id 全部在溢出集（无活跃区条目）", arch1.All(c => c.Id.StartsWith("b4") || int.Parse(c.Id[1..]) < 20), Line);
            storage.RollToArchive(big); // 第二次同批 → 不重复追加
            var arch2 = storage.LoadArchive();
            Check("归档 id 去重防翻倍（仍 20 条）", arch2.Count == 20, Line);

            // Create → LoadClips round-trip：字段无损 + camelCase JSON
            var fileStore = new FileStore(dir);
            var svc = new ClipService(storage, fileStore);
            var created = svc.Create("link", "", null, "<b>x</b>", "https://ex.com/s?utm_source=ad&keep=1",
                new List<string> { "标签", "标签", "  ", "标签" }, "7d");
            var loaded = storage.LoadClips();
            Check("Create 自动标题用清理后 url 前 60", created.Title == "https://ex.com/s?keep=1", Line);
            Check("Create link url 已去追踪参数", created.Url == "https://ex.com/s?keep=1", Line);
            Check("Create tags 去重过滤空", string.Join(",", created.Tags) == "标签", Line);
            Check("Create 过期 7d > now", created.ExpireAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Line);
            Check("Create html 保留", created.Html == "<b>x</b>", Line);
            Check("round-trip 1 条", loaded.Count == 1, Line);
            Check("round-trip id/type/title 无损", loaded[0].Id == created.Id && loaded[0].Type == "link" && loaded[0].Title == created.Title, Line);
            Check("round-trip content 默认空串", loaded[0].Content == "", Line);
            Check("round-trip pinned/copyCount 默认", !loaded[0].Pinned && loaded[0].CopyCount == 0, Line);
            var raw = File.ReadAllText(storage.ClipsFile);
            Check("JSON camelCase（\"clips\" / \"expireAt\" / \"copyCount\"）",
                raw.Contains("\"clips\"") && raw.Contains("\"expireAt\"") && raw.Contains("\"copyCount\"") && raw.Contains("\"createdAt\""), Line);
            Check("JSON 文档含 app/version", raw.Contains("\"app\"") && raw.Contains("\"version\""), Line);

            // ResolveExpire / SanitizeInput / IsExpired
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Check("resolveExpire '' → null", ClipService.ResolveExpire("") == null, Line);
            Check("resolveExpire '2h' ≈ +2h", ClipService.ResolveExpire("2h") is { } h && Math.Abs(h - (now + 7_200_000L)) < 2000, Line);
            Check("resolveExpire 畸形 'xyz' → null", ClipService.ResolveExpire("xyz") == null, Line);
            var (t, tgs) = ClipService.SanitizeInput("  标题  ", new List<string> { "a", "b", "a", "", "c" });
            Check("sanitizeInput title trim", t == "标题", Line);
            Check("sanitizeInput tags 去重保序", string.Join(",", tgs) == "a,b,c", Line);
            Check("isExpired 永久 false", !ClipService.IsExpired(Make("e1", expire: null)), Line);
            Check("isExpired 过期 true", ClipService.IsExpired(Make("e2", expire: now - 1)), Line);

            // Search：类型过滤 + 拼音匹配（title+tags，对齐 getVisibleClips）+ 过期过滤
            storage.SaveClips(new List<ClipItem>
            {
                Make("s1", type: "text", title: "身份验证", upd: 300),
                Make("s2", type: "link", url: "https://x.com", content: "其他", upd: 200),
            });
            Check("Search 拼音 'sf' 命中 s1", svc.Search("sf").Select(c => c.Id).SequenceEqual(new[] { "s1" }), Line);
            Check("Search type=link 仅 s2", svc.Search(null, type: "link").Select(c => c.Id).SequenceEqual(new[] { "s2" }), Line);
            Check("Search q=身份 命中 s1", svc.Search("身份").Select(c => c.Id).SequenceEqual(new[] { "s1" }), Line);

            // ---- M3a 增量：Format 展示纯函数（对齐 app.js fmtSize/fmtTime/expLabel/hostOf/looksLikeJson）----
            Check("fmtSize 0 → 0B", Format.Size(0) == "0B", Line);
            Check("fmtSize 512 → 512B", Format.Size(512) == "512B", Line);
            Check("fmtSize 2048 → 2.0KB", Format.Size(2048) == "2.0KB", Line);
            Check("fmtSize 3MB → 3.0MB", Format.Size(3L * 1048576) == "3.0MB", Line);
            Check("fmtTime 0 → 空串", Format.Time(0) == "", Line);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Check("fmtTime now 非空", Format.Time(ts).Length > 0, Line);
            Check("expLabel null → 空串", Format.ExpLabel(null) == "", Line);
            Check("expLabel 已过期", Format.ExpLabel(ts - 1000) == "已过期", Line);
            Check("expLabel 分钟级（+5min）", Format.ExpLabel(ts + 300000) == "5 分钟后过期", Line);
            Check("expLabel 小时级（+2h）", Format.ExpLabel(ts + 7200000) == "2 小时后过期", Line);
            Check("expLabel 天级（+3d）", Format.ExpLabel(ts + 3L * 86400000) == "3 天后过期", Line);
            Check("hostOf 标准 URL", Format.HostOf("https://sub.example.com/p?a=1") == "sub.example.com", Line);
            Check("hostOf 畸形回退", Format.HostOf("not-a-url") == "not-a-url", Line);
            Check("looksLikeJson 对象 true", Format.LooksLikeJson("{\"a\":1}"), Line);
            Check("looksLikeJson 数组 true", Format.LooksLikeJson("[1,2]"), Line);
            Check("looksLikeJson 普通文本 false", !Format.LooksLikeJson("hello"), Line);
            Check("looksLikeJson 畸形 JSON false", !Format.LooksLikeJson("{oops"), Line);
            Check("PrettyJson 缩进 2", Format.PrettyJson("{\"a\":1}") == "{\n  \"a\": 1\n}", Line);
            Check("PrettyJson 失败原样", Format.PrettyJson("abc") == "abc", Line);
            Check("AutoTitle 首行前 20 字", Format.AutoTitle("第一行标题\n第二行") == "第一行标题", Line);
            Check("AutoTitle 超长截 20", Format.AutoTitle(new string('字', 30)).Length == 20, Line);

            // LayoutRules.ColumnsFor（对齐 .list auto-fill minmax(280px,1fr) + 钳制 1~4）
            Check("列数 260px → 1", LayoutRules.ColumnsFor(260) == 1, Line);
            Check("列数 600px → 2", LayoutRules.ColumnsFor(600) == 2, Line);
            Check("列数 900px → 3", LayoutRules.ColumnsFor(900) == 3, Line);
            Check("列数 1200px → 4", LayoutRules.ColumnsFor(1200) == 4, Line);
            Check("列数 超大钳制 4", LayoutRules.ColumnsFor(4000) == 4, Line);

            // ---- M3a 增量：ClipService 变更（pin/copy/update/delete/archive 对齐 Web API 语义）----
            var m1 = svc.Create("text", "变更测试", "原始内容", null, null, null, null);
            Check("GetById 命中", svc.GetById(m1.Id)?.Content == "原始内容", Line);
            Check("TogglePin → true", svc.TogglePin(m1.Id), Line);
            var pinned = svc.GetById(m1.Id);
            Check("TogglePin 落库 pinned", pinned != null && pinned.Pinned, Line);
            Check("BumpCopyCount → 1", svc.BumpCopyCount(m1.Id) == 1, Line);
            var bumped = svc.GetById(m1.Id);
            Check("BumpCopyCount 落库 copyCount", bumped != null && bumped.CopyCount == 1, Line);
            var upd = svc.Update(m1.Id, " 新标题 ", new List<string> { "a", "b", "a" }, "1h", content: "新内容");
            Check("Update 返回非空", upd != null, Line);
            Check("Update title 净化 trim", upd!.Title == "新标题", Line);
            Check("Update tags 去重", string.Join(",", upd.Tags) == "a,b", Line);
            Check("Update content 更新", upd.Content == "新内容", Line);
            Check("Update expire 1h 解析", upd.ExpireAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Line);
            Check("Update updatedAt 更新", upd.UpdatedAt >= m1.UpdatedAt, Line);
            Check("Update 空内容拒绝（抛异常）", Throws(() => svc.Update(m1.Id, "", null, null, content: "")), Line);
            svc.Archive(m1.Id);
            Check("Archive 后活跃区无此条", svc.GetById(m1.Id) == null, Line);
            Check("Archive 后归档含此条", storage.LoadArchive().Any(c => c.Id == m1.Id), Line);
            var m2 = svc.Create("text", "删除测试", "将被删除", null, null, null, null);
            svc.Delete(m2.Id);
            Check("Delete 后不存在", svc.GetById(m2.Id) == null, Line);
            // 链接更新：非法 url 拒绝、合法 url 清理追踪参数
            var m3 = svc.Create("link", "", null, null, "https://x.com/p?a=1", null, null);
            Check("Update link 非法 url 抛异常", Throws(() => svc.Update(m3.Id, "", null, null, url: "javascript:alert(1)")), Line);
            var m3b = svc.Update(m3.Id, "", null, null, url: "https://x.com/q?utm_source=ad&keep=1");
            Check("Update link url 清理", m3b != null && m3b.Url == "https://x.com/q?keep=1", Line);
            Check("Update link 自动标题", m3b != null && m3b.Title == "https://x.com/q?keep=1", Line);

            // ---- M3b-1 增量：Unarchive 闭环 + LayoutRules.ColumnsFor maxColumns 参数 ----
            // Unarchive：归档条目恢复到活跃区，updatedAt 刷新
            var unarchBefore = svc.GetById(m1.Id);
            Check("Unarchive 前活跃区无 m1", unarchBefore == null, Line);
            var unarchOk = svc.Unarchive(m1.Id);
            Check("Unarchive 返回 true", unarchOk, Line);
            var unarchAfter = svc.GetById(m1.Id);
            Check("Unarchive 后活跃区含 m1", unarchAfter != null, Line);
            Check("Unarchive 后归档区无 m1", !storage.LoadArchive().Any(c => c.Id == m1.Id), Line);
            Check("Unarchive 失败：归档中不存在", Throws(() => svc.Unarchive("not-exist-id")), Line);
            // 重复 Unarchive：m1 已在活跃区，归档已无 → 抛错（防御性）
            Check("Unarchive 重复失败：归档已移走", Throws(() => svc.Unarchive(m1.Id)), Line);
            // 重复归档（活跃区有重）→ 返回 false 防御
            svc.Archive(m1.Id); // 再次归档
            var dupId = m1.Id; svc.Create("text", "dup", "x", null, null, null, null); // 活跃区先放个同 id 模拟
            // 上面 Create 是新 id 不会冲突；用另一种方式：直接在活跃区插入 m1 同 id 的条目
            var live = storage.LoadClips(); live.Add(new ClipItem { Id = m1.Id, Type = "text", Content = "x" });
            storage.SaveClips(live);
            var dup = svc.Unarchive(m1.Id);
            Check("Unarchive 活跃区已存在返回 false", dup == false, Line);

            // LayoutRules.ColumnsFor maxColumns：0/负=自动 4 上限，1~4=锁定上限
            Check("列数 maxColumns=0 与无参等效（1200px→4）", LayoutRules.ColumnsFor(1200, 0) == 4, Line);
            Check("列数 maxColumns=1（1200px 锁 1 列）", LayoutRules.ColumnsFor(1200, 1) == 1, Line);
            Check("列数 maxColumns=2（900px 锁 2 列）", LayoutRules.ColumnsFor(900, 2) == 2, Line);
            Check("列数 maxColumns=3（1500px 钳到 3）", LayoutRules.ColumnsFor(1500, 3) == 3, Line);
            Check("列数 maxColumns=-1 视作自动（2000px→4）", LayoutRules.ColumnsFor(2000, -1) == 4, Line);
            Check("列数 maxColumns=99 钳到 4（1500px）", LayoutRules.ColumnsFor(1500, 99) == 4, Line);
            Check("列数 maxColumns=2（260px 仍 1 列——下限生效）", LayoutRules.ColumnsFor(260, 2) == 1, Line);

            // ---- M3b-2a 增量：FileStore 文件实体（对齐 files.js saveFile/getFilePath/deleteFile）+ Format.FileKindFor ----
            var fs = new FileStore(dir);
            var fbuf = System.Text.Encoding.UTF8.GetBytes("hello clipboard");
            var (fid1, fname1, fsize1, fmime1) = fs.Save(fbuf, "报告.txt", "text/plain");
            Check("Save fileId 为 UUID", Guid.TryParse(fid1, out _), Line);
            Check("Save fileName 原名保留", fname1 == "报告.txt", Line);
            Check("Save fileSize = 字节数", fsize1 == fbuf.Length, Line);
            Check("Save mime 小写化", fmime1 == "text/plain", Line);
            Check("GetPath 指向存在的文件", File.Exists(fs.GetPath(fid1)), Line);
            Check("GetPath 扩展名走 EXT_BY_MIME（text/plain→txt）", fs.GetPath(fid1).EndsWith(".txt"), Line);
            var (fid2, _, _, _) = fs.Save(fbuf, "数据.dat", ""); // 未知 mime → 原始安全扩展名兜底
            Check("未知 mime 兜底原扩展名 .dat", fs.GetPath(fid2).EndsWith(".dat"), Line);
            var (fid3, _, _, _) = fs.Save(fbuf, "x.abcdefghi", ""); // 扩展名超 8 位不安全 → bin
            Check("不安全扩展名回退 bin", fs.GetPath(fid3).EndsWith(".bin"), Line);
            Check("空 buffer 拒绝", Throws(() => fs.Save(Array.Empty<byte>(), "a.txt", "")), Line);
            Check("10MB 上限拒绝", Throws(() => fs.Save(new byte[FileStore.MaxFileSize + 1], "big.bin", "application/octet-stream")), Line);
            Check("黑名单 mime text/html 拒绝", Throws(() => fs.Save(fbuf, "a.html", "text/html")), Line);
            Check("黑名单扩展名 exe 拒绝", Throws(() => fs.Save(fbuf, "a.exe", "application/octet-stream")), Line);
            Check("黑名单扩展名 bat 拒绝", Throws(() => fs.Save(fbuf, "a.bat", "")), Line);
            fs.Delete(fid2);
            Check("Delete 后 GetPath 抛异常", Throws(() => fs.GetPath(fid2)), Line);
            Check("Delete 不存在静默", !Throws(() => fs.Delete("00000000-0000-0000-0000-000000000000")), Line);
            Check("Exists true/false", fs.Exists(fid1) && !fs.Exists("00000000-0000-0000-0000-000000000000"), Line);
            // 图片 mime 判定（2a 弹窗拒收依据）
            Check("MimeFromPath png → image/png", FileStore.MimeFromPath("a.png") == "image/png", Line);
            Check("MimeFromPath 未知 → 空串", FileStore.MimeFromPath("a.xyz") == "", Line);
            // Format.FileKindFor（对齐 makeFileIcon：PDF/ZIP 判定）
            Check("FileKindFor .pdf → pdf", Format.FileKindFor("报告.PDF") == "pdf", Line);
            Check("FileKindFor .zip → zip", Format.FileKindFor("a.zip") == "zip", Line);
            Check("FileKindFor .rar → zip", Format.FileKindFor("a.RAR") == "zip", Line);
            Check("FileKindFor .tar.gz → zip", Format.FileKindFor("a.tar.gz") == "zip", Line);
            Check("FileKindFor 其他 → file", Format.FileKindFor("a.txt") == "file", Line);
            Check("FileKindFor 无扩展名 → file", Format.FileKindFor("file") == "file", Line);
            // Create file 类型：字段落库（对齐 createClip file 分支）
            var fc = svc.Create("file", "", null, null, null, null, null, fid1, "报告.txt", fsize1, "text/plain");
            Check("Create file 标题 = 空（兜底在 UI 层取文件名）", fc.Title == "", Line);
            Check("Create file 字段落库", fc.FileId == fid1 && fc.FileName == "报告.txt" && fc.FileSize == fsize1 && fc.FileMime == "text/plain", Line);
            Check("Create file 缺 fileId 拒绝", Throws(() => svc.Create("file", "", null, null, null, null, null)), Line);

            // ---- M3b-2b 增量：图片线（对齐 Web png/jpg/gif/webp + IsImageMime + FileStore 实体落库） ----
            // PNG: 最小合法 1x1 PNG 字节（67 字节）→ 验证解码 + 字段落库 + 扩展名映射
            var png1x1 = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };
            var (imgFid, imgName, imgSize, imgMime) = fs.Save(png1x1, "screenshot.png", "image/png");
            Check("Save PNG fileMime 字段落库", imgMime == "image/png" && imgName == "screenshot.png" && imgSize == png1x1.Length, Line);
            Check("Save PNG 扩展名映射 → .png", fs.GetPath(imgFid).EndsWith(".png"), Line);
            Check("ReadAllBytes PNG 字节一致", fs.ReadAllBytes(imgFid).Length == png1x1.Length && fs.ReadAllBytes(imgFid)[0] == 0x89, Line);
            var (jpgFid, _, _, jpgMime) = fs.Save(png1x1, "a.jpg", "image/jpeg"); // 字节不是真 JPEG 但 Save 接受（解码由 CardView 试错）
            Check("Save JPG 扩展名映射 → .jpg", fs.GetPath(jpgFid).EndsWith(".jpg"), Line);
            Check("Save JPG mime 小写化", jpgMime == "image/jpeg", Line);
            var (gifFid, _, _, _) = fs.Save(png1x1, "a.gif", "image/gif");
            Check("Save GIF 扩展名映射 → .gif", fs.GetPath(gifFid).EndsWith(".gif"), Line);
            var (webpFid, _, _, _) = fs.Save(png1x1, "a.webp", "image/webp");
            Check("Save WEBP 扩展名映射 → .webp", fs.GetPath(webpFid).EndsWith(".webp"), Line);
            // BMP: image/bmp 不在 ExtByMime → 原始安全扩展名兜底
            var (bmpFid, _, _, _) = fs.Save(png1x1, "a.bmp", "image/bmp");
            Check("Save image/bmp 未知 mime 兜底原扩展名 .bmp", fs.GetPath(bmpFid).EndsWith(".bmp"), Line);
            // 大于 10MB 拒收
            Check("10MB 图片拒收", Throws(() => fs.Save(new byte[FileStore.MaxFileSize + 1], "big.png", "image/png")), Line);
            // Format.IsImageMime（M3b-2b：卡片/弹窗图片分流依据，对齐 Web handleCardClick/bindImageHoverPreview 判定）
            Check("IsImageMime image/png → true", Format.IsImageMime("image/png"), Line);
            Check("IsImageMime image/jpeg → true", Format.IsImageMime("image/jpeg"), Line);
            Check("IsImageMime text/plain → false", !Format.IsImageMime("text/plain"), Line);
            Check("IsImageMime null → false", !Format.IsImageMime(null), Line);
            // FileStore.MimeFromPath 图片扩展名判定（图片判定供 2b 弹窗分流）
            Check("MimeFromPath jpg → image/jpeg", FileStore.MimeFromPath("a.jpg") == "image/jpeg", Line);
            Check("MimeFromPath jpeg → image/jpeg", FileStore.MimeFromPath("a.jpeg") == "image/jpeg", Line);
            Check("MimeFromPath gif → image/gif", FileStore.MimeFromPath("a.gif") == "image/gif", Line);
            Check("MimeFromPath webp → image/webp", FileStore.MimeFromPath("a.webp") == "image/webp", Line);
            // Create file 字段带图片 mime（落入 ClipItem 用于 CardView 图片卡体判定）
            var imgClip = svc.Create("file", "截图", null, null, null, null, null, imgFid, "screenshot.png", imgSize, "image/png");
            Check("Create file mime=image/png 字段落库", imgClip.FileMime == "image/png" && imgClip.FileId == imgFid, Line);

            // ---- M3b-3a 增量：批量编辑数据层（BatchDelete / BatchSetTags）----
            // 准备：活跃区 2 条（b1 文本, b2 文件），归档区 1 条（b3 文件）
            storage.SaveClips(new List<ClipItem>
            {
                new ClipItem { Id = "b1", Type = "text", Content = "x", UpdatedAt = 100, CreatedAt = 100, Tags = new() { "t1" } },
                new ClipItem { Id = "b2", Type = "file", FileId = fid1, FileName = "报告.txt", UpdatedAt = 200, CreatedAt = 200, Tags = new() { "t1", "t2" } },
            });
            storage.SaveArchive(new List<ClipItem>
            {
                new ClipItem { Id = "b3", Type = "file", FileId = imgFid, FileName = "screenshot.png", UpdatedAt = 300, CreatedAt = 300, Tags = new() { "t2" } },
            });
            var fileBefore = File.Exists(fileStore.GetPath(fid1));
            var deleted = svc.BatchDelete(new[] { "b1", "b3" });
            Check("BatchDelete 跨活跃+归档删除 2 条", deleted == 2, Line);
            Check("BatchDelete 后活跃区剩 b2", storage.LoadClips().Select(c => c.Id).SequenceEqual(new[] { "b2" }), Line);
            Check("BatchDelete 后归档区为空", storage.LoadArchive().Count == 0, Line);
            Check("BatchDelete 清理 b3 文件实体", !fileStore.Exists(imgFid), Line);
            Check("BatchDelete 保留 b2 文件实体", fileStore.Exists(fid1) == fileBefore, Line);
            Check("BatchDelete 记录墓碑 b1+b3", storage.LoadTombstones().Select(t => t.Id).OrderBy(x => x).SequenceEqual(new[] { "b1", "b3" }), Line);
            Check("BatchDelete 空 ids 抛异常", Throws(() => svc.BatchDelete(Array.Empty<string>())), Line);

            // 批量加标签：b2 加 t3/t4，updatedAt 刷新
            var beforeAdd = storage.LoadClips().First(c => c.Id == "b2").UpdatedAt;
            var affectedAdd = svc.BatchSetTags(new[] { "b2" }, new[] { "t3", "t4", "t1" }, true);
            Check("BatchSetTags add affected=1", affectedAdd == 1, Line);
            var b2AfterAdd = storage.LoadClips().First(c => c.Id == "b2");
            Check("BatchSetTags add tags 去重上限", string.Join(",", b2AfterAdd.Tags) == "t1,t2,t3,t4", Line);
            Check("BatchSetTags add updatedAt 刷新", b2AfterAdd.UpdatedAt > beforeAdd, Line);

            // 批量减标签：b2 减 t1/t2，保留 t3/t4
            var affectedRm = svc.BatchSetTags(new[] { "b2" }, new[] { "t1", "t2" }, false);
            Check("BatchSetTags remove affected=1", affectedRm == 1, Line);
            var b2AfterRm = storage.LoadClips().First(c => c.Id == "b2");
            Check("BatchSetTags remove 结果", string.Join(",", b2AfterRm.Tags) == "t3,t4", Line);
            Check("BatchSetTags 空 ids 抛异常", Throws(() => svc.BatchSetTags(Array.Empty<string>(), new[] { "x" }, true)), Line);
            Check("BatchSetTags 空 tags 抛异常", Throws(() => svc.BatchSetTags(new[] { "b2" }, Array.Empty<string>(), true)), Line);

            // 批量加标签 MAX_TAGS 上限：b2 已有 t3,t4，再加 t5~t13（9 个）→ 保留前 10
            b2AfterRm.Tags = new List<string> { "t3", "t4" };
            storage.SaveClips(new List<ClipItem> { b2AfterRm });
            svc.BatchSetTags(new[] { "b2" }, new[] { "t5", "t6", "t7", "t8", "t9", "t10", "t11", "t12", "t13" }, true);
            Check("BatchSetTags add MAX_TAGS 上限 10", storage.LoadClips().First(c => c.Id == "b2").Tags.Count == 10, Line);
        }
        catch (Exception ex)
        {
            _fail++;
            Line("[FAIL] 未捕获异常: " + ex);
        }

        Line($"=== {(_fail == 0 ? "ALL PASS" : $"{_fail} FAILED")} ===");
        // 双落盘：临时目录 + 当前目录（GUI 无控制台，日志是唯一可见载体；当前目录 = 运行目录，必可写）
        try { File.WriteAllText(logFile, log.ToString()); } catch { /* 日志失败不影响退出码 */ }
        try { File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "selftest.log"), log.ToString()); } catch { /* 同上 */ }
        return _fail == 0 ? 0 : 1;
    }

    private static ClipItem Make(string id, string type = "text", bool pinned = false, long copy = 0,
                                 long upd = 0, long created = 0, string? content = null, string? url = null, long? expire = null,
                                 string? title = null)
    {
        return new ClipItem
        {
            Id = id, Type = type, Pinned = pinned, CopyCount = copy,
            UpdatedAt = upd, CreatedAt = created,
            Content = content ?? "", Url = url ?? "", Title = title ?? "",
            ExpireAt = expire,
        };
    }

    private static void Check(string name, bool cond, Action<string> line)
    {
        if (cond) { line("[PASS] " + name); }
        else { _fail++; line("[FAIL] " + name); }
    }

    private static bool Throws(Action act)
    {
        try { act(); return false; }
        catch { return true; }
    }
}
