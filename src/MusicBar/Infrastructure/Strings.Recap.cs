using System.Collections.Generic;

namespace MusicBar.Infrastructure;

public static partial class Strings
{
    /// <summary>The year in music: its card and the end-of-year banner.</summary>
    static partial void AddRecap(Dictionary<string, (string En, string Vi)> d)
    {
        d.Add("Menu_Recap", ("Your year in music…", "Tổng kết năm âm nhạc…"));

        d.Add("Rec_WindowTitle", ("MusicBar: your year in music", "MusicBar: tổng kết năm âm nhạc"));
        d.Add("Rec_Kicker", ("MUSICBAR · YEAR IN MUSIC", "MUSICBAR · TỔNG KẾT NĂM"));
        d.Add("Rec_Subtitle", ("Your year, song by song", "Một năm của bạn, qua từng bài hát"));
        d.Add("Rec_HoursUnit", ("hours of music", "giờ nghe nhạc"));
        d.Add("Rec_MinutesUnit", ("minutes of music", "phút nghe nhạc"));
        d.Add("Rec_HoursMinutes", ("{0} h {1} min", "{0} giờ {1} phút"));
        d.Add("Rec_Minutes", ("{0} min", "{0} phút"));
        d.Add("Rec_Counts", ("{0} plays · {1} songs · {2} artists", "{0} lượt nghe · {1} bài · {2} nghệ sĩ"));
        d.Add("Rec_TopSongs", ("TOP SONGS", "TOP BÀI HÁT"));
        d.Add("Rec_TopArtists", ("ARTISTS YOU LOVED", "NGHỆ SĨ RUỘT"));
        d.Add("Rec_Plays", ("{0} plays", "{0} lượt"));
        d.Add("Rec_BigDay", ("BIGGEST DAY", "NGÀY CHÁY NHẤT"));
        d.Add("Rec_GoldenHour", ("GOLDEN HOUR", "GIỜ VÀNG"));
        d.Add("Rec_GoldenHourSub", ("until {0}:00", "đến {0}:00"));
        d.Add("Rec_Streak", ("LONGEST STREAK", "CHUỖI DÀI NHẤT"));
        d.Add("Rec_Days", ("{0} days", "{0} ngày"));
        d.Add("Rec_DaysListened", ("{0} days with music", "{0} ngày có nhạc"));
        d.Add("Rec_App", ("MOSTLY ON", "NGHE NHIỀU NHẤT"));
        d.Add("Rec_YouAre", ("YOU ARE", "BẠN LÀ"));
        AddRecapTitles(d);
        d.Add("Rec_Empty", ("Nothing counted for {0} yet. Play some music and come back!", "Chưa đếm được bài nào trong năm {0}. Nghe nhạc rồi quay lại nhé!"));
        d.Add("Rec_Note", ("Counted on this computer only. Share it if you like!", "Chỉ đếm trên máy này. Thích thì khoe với ae!"));
        d.Add("Rec_Save", ("Save picture", "Lưu ảnh"));
        d.Add("Rec_Copy", ("Copy", "Copy ảnh"));
        d.Add("Rec_Saved", ("Saved. Ready to post!", "Lưu rồi, đăng thôi!"));
        d.Add("Rec_SaveFailed", ("Couldn't save the picture.", "Không lưu được ảnh."));
        d.Add("Rec_Copied", ("Copied. Paste it into a post (Ctrl+V).", "Đã copy, dán vào bài đăng là được (Ctrl+V)."));

        d.Add("RecB_Title", ("Your {0} in music is ready", "Tổng kết năm {0} của bạn đã có"));
        d.Add("RecB_Body", ("{0} of music this year. See your top songs and what kind of listener you are.", "Năm nay bạn nghe {0} nhạc. Xem top bài hát và bạn thuộc kiểu người nghe nào nhé."));
        d.Add("RecB_Open", ("Show me", "Xem ngay"));
        d.Add("RecB_Later", ("Later", "Để sau"));
        d.Add("RecB_Never", ("Don't remind me this year", "Không nhắc lại năm nay"));
    }

    /// <summary>
    /// The titles a year can earn (Services.RecapTitles): RecT_ the title, RecL_ its line, RecBadge_ the short
    /// badge when it isn't the main one. The {0}-style holes are filled with the year's own numbers and names.
    /// </summary>
    static void AddRecapTitles(Dictionary<string, (string En, string Vi)> d)
    {
        void T(string key, int v, (string En, string Vi) title, (string En, string Vi) line)
        {
            d.Add($"RecT_{key}_{v}", title);
            d.Add($"RecL_{key}_{v}", line);
        }
        void B(string key, (string En, string Vi) badge) => d.Add($"RecBadge_{key}", badge);

        // The Vietnamese is written, not translated: rhymes, twisted sayings ("một ngày không gặp như ba thu"),
        // sound-alikes (lofi, lâu phai), and the "X chảy trong máu" shape. The English plays its own games.

        // when
        T("NightOwl", 0, ("No sleep, just beats", "Đêm không ngủ, nhạc không ngừng"),
            ("{0}% of your music played after 10 pm. Who's going to work tomorrow?", "{0}% nhạc của bạn phát sau 10 giờ tối. Mai ai đi làm thay?"));
        T("NightOwl", 1, ("Certified night owl", "Cú đêm chính hiệu"),
            ("{0}% of your listening was after 10 pm. Go to bed. (You won't.)", "{0}% thời gian nghe là sau 10 giờ tối. Đi ngủ đi bạn ơi."));
        B("NightOwl", ("Night owl", "Cú đêm"));
        T("Vampire", 0, ("Vampire with taste", "Ma cà rồng có gu"),
            ("{0} hours of music between 1 and 4 am. Sunlight? Never heard of her.", "{0} giờ nhạc từ 1 đến 4 giờ sáng. Ánh nắng là gì, ăn được không?"));
        T("Vampire", 1, ("Can't wake up, won't go to sleep", "Sáng không dậy nổi, đêm không ngủ nổi"),
            ("{0} hours between 1 and 4 am. Bold of you to have a morning tomorrow.", "Giờ này còn thức thì một là nhớ người, hai là nghe nhạc. Bạn có {0} giờ."));
        B("Vampire", ("Vampire hours", "Ma cà rồng"));
        T("EarlyBird", 0, ("Up at dawn, music on", "Sáng sớm tinh mơ, nhạc đã vu vơ"),
            ("{0}% of your music played between 5 and 9 am. Even the rooster is impressed.", "{0}% nhạc phát từ 5 đến 9 giờ sáng. Gà còn chưa kịp gáy."));
        T("EarlyBird", 1, ("The early bird gets the beat", "Dậy sớm bắt được beat"),
            ("Early birds catch worms, you catch beats: {0}% of your music played before 9 am.", "Chim dậy sớm bắt sâu, bạn dậy sớm bắt beat: {0}% nhạc phát trước 9 giờ sáng."));
        B("EarlyBird", ("Early bird", "Dậy sớm bắt beat"));
        T("NineToFive", 0, ("Work is a side quest", "Đi làm là phụ, nghe nhạc là chính"),
            ("Office hours are peak headphone hours. Your boss doesn't need to know.", "Giờ hành chính là giờ cao điểm của tai nghe. Sếp không cần biết đâu."));
        T("NineToFive", 1, ("Salary pending, playlist ascending", "Lương chưa lên, nhạc đã lên"),
            ("Most of your music plays at work, on {0}. Every deadline gets a soundtrack.", "Phần lớn nhạc chạy trong giờ làm, trên {0}. Deadline nào cũng có nhạc nền."));
        B("NineToFive", ("Office vibes", "Đi làm để nghe nhạc"));
        T("LunchBreak", 0, ("Lunch with a side of bangers", "Cơm trưa có nhạc, ngủ trưa có beat"),
            ("11 to 2 is when the music really starts. Rice first, then bangers.", "11 giờ tới 2 giờ chiều là giờ vàng. Cơm xong là quẩy."));
        B("LunchBreak", ("Lunch DJ", "DJ giờ trưa"));
        T("Weekend", 0, ("Saturday party, Sunday recovery", "Thứ Bảy quẩy, Chủ nhật bay"),
            ("{0}% of your music lands on the weekend. Weekdays are for recharging.", "{0}% nhạc dồn hết vào cuối tuần. Ngày thường là để dành sức."));
        T("Weekend", 1, ("Weekend warrior", "Chiến thần cuối tuần"),
            ("Weekends are for music: {0}% of your listening.", "Cuối tuần mới là lúc mở nhạc thật sự: {0}% thời gian nghe."));
        B("Weekend", ("Weekend warrior", "Thứ Bảy quẩy"));
        T("MondayBlues", 0, ("Monday needs a soundtrack", "Thứ Hai ngán ngẩm, nhạc phải thật đậm"),
            ("Monday is your biggest day ({0}%). We get it.", "Thứ Hai là ngày bạn nghe nhiều nhất ({0}%). Hiểu mà."));
        B("MondayBlues", ("Monday survivor", "Cứu thứ Hai"));

        // one song, one artist
        T("RentFree", 0, ("{0} lives rent-free in your head", "{0} ở trọ không trả tiền"),
            ("{1} plays. It should start paying rent.", "{1} lần nghe. Bài này nên trả tiền nhà cho bạn."));
        T("RentFree", 1, ("Practice makes perfect lyrics", "Có công replay, có ngày thuộc lời"),
            ("{0}, {1} times. You don't need the lyrics anymore.", "{0} được bật {1} lần. Giờ hát không cần nhìn lời."));
        B("RentFree", ("On repeat", "Nghe lặp"));
        T("Replay", 0, ("Replayed to dust", "Replay tới mòn, nghe tới mỏi"),
            ("{0}: {1} plays. At this point it's meditation.", "{0}: {1} lượt. Tới mức này là thiền rồi, không phải nghe nhạc."));
        T("Replay", 1, ("The replay button asked for a day off", "Nút replay cũng xin nghỉ phép"),
            ("{0}, {1} times. That song is part of your personality now.", "{1} lần {0}. Bài này giờ là một phần con người bạn."));
        B("Replay", ("{1} plays, one song", "{1} lượt một bài"));
        T("Stan", 0, ("{0} is the one", "{0} là chân ái"),
            ("{0} took {1}% of your listening. More loyal than most relationships.", "{0} chiếm {1}% thời gian nghe. Chung thủy hơn cả người yêu."));
        T("Stan", 1, ("Ride or die for {0}", "Một lòng một dạ với {0}"),
            ("{1}% of your year was {0}. Everyone else was just a fling.", "{1}% năm nay là {0}. Nghệ sĩ khác chỉ là cho vui."));
        B("Stan", ("{0} stan", "Fan cứng {0}"));
        T("Fandom", 0, ("Certified {0}", "{0} chính hiệu"),
            ("{1} took {2}% of your listening. Membership card approved.", "{1} chiếm {2}% thời gian nghe. Thẻ thành viên đã được duyệt."));
        T("Fandom", 1, ("{0}, the real deal", "{0} hàng thật, không phải hàng nhái"),
            ("{2}% of your year went to {1}. And you'd do it again.", "{2}% năm nay dành cho {1}. Thương là thương thật."));
        B("Fandom", ("{0}", "{0}"));

        // how wide
        T("Explorer", 0, ("The algorithm calls you for advice", "Thuật toán gọi bằng điện thoại"),
            ("{0} songs, all found by you. The algorithm has to call ahead before suggesting anything.", "{0} bài khác nhau, bài nào cũng tự tìm. Thuật toán muốn gợi ý phải gọi điện hẹn trước."));
        T("Explorer", 1, ("Certified crate digger", "Đi một ngày đàng, nghe một sàng nhạc"),
            ("{0} songs in one year, barely any repeats. Your taste never sits still.", "{0} bài trong một năm, hiếm bài nào nghe lại. Gu bạn không bao giờ đứng yên."));
        B("Explorer", ("Crate digger", "Săn nhạc mới"));
        T("Buffet", 0, ("A heart with {0} rooms", "Trái tim có {0} ngăn"),
            ("{0} different artists, each with a spot. Plenty of love, no drama.", "{0} nghệ sĩ khác nhau, ai cũng có một chỗ. Đa tình nhưng không đa sầu."));
        T("Buffet", 1, ("Your taste is a buffet", "Gu nhạc như buffet, gì cũng gắp"),
            ("{0} different artists. A little bit of everything.", "{0} nghệ sĩ, món nào cũng nếm một chút."));
        B("Buffet", ("{0} artists", "Tim {0} ngăn"));
        T("TinyLoop", 0, ("Small playlist, big feelings", "Playlist nhỏ, tâm hồn to"),
            ("A whole year on {0} songs. When you love something, you commit.", "Cả năm quanh quẩn {0} bài. Đã thương là thương lâu."));
        B("TinyLoop", ("Small rotation", "Ít bài mà sâu"));

        // how much
        T("NoDaysOff", 0, ("{0}-day streak, unbroken", "Chuỗi {0} ngày chưa gãy"),
            ("Even the Duolingo owl is jealous.", "Con cú Duolingo nhìn chuỗi này cũng phải nể."));
        T("NoDaysOff", 1, ("Even the Duolingo owl is jealous", "Duolingo nhìn cũng phải nể"),
            ("{0} days in a row with music. Not one skipped.", "{0} ngày liền ngày nào cũng có nhạc. Không bỏ buổi nào."));
        B("NoDaysOff", ("{0}-day streak", "Chuỗi {0} ngày"));
        T("EveryDay", 0, ("A day without music feels like a year", "Một ngày không nhạc như ba thu"),
            ("{0} days of the year had music. The others felt very long.", "{0} ngày trong năm có nhạc. Mấy ngày còn lại dài lê thê."));
        B("EveryDay", ("{0} days of music", "{0} ngày có nhạc"));
        T("Marathon", 0, ("{0}: a marathon for the ears", "Ngày {0}: chạy marathon bằng tai"),
            ("{1} hours of music in one day. What happened that day?", "{1} giờ nhạc trong một ngày. Hôm đó có chuyện gì vậy?"));
        T("Marathon", 1, ("{0}, the legendary day", "Ngày {0} huyền thoại"),
            ("{1} hours in a single day. Your headphones want a vacation.", "Một ngày, {1} giờ nhạc. Tai nghe xin nghỉ phép."));
        B("Marathon", ("{1}-hour day", "Ngày {1} giờ nhạc"));
        T("Headphones", 0, ("70% of your body is music", "70% cơ thể là nhạc"),
            ("{0} hours of music, that's {1} days straight. The other 30% is snacks.", "{0} giờ nhạc, tức là {1} ngày liền không tháo tai nghe. 30% còn lại là cơm."));
        T("Headphones", 1, ("Headphones first, life second", "Tai nghe là chính, đời là phụ"),
            ("{0} hours this year. Your headphones deserve a raise.", "{0} giờ nhạc năm nay. Tai nghe đáng được tăng lương."));
        B("Headphones", ("{0} hours", "70% là nhạc"));
        T("Sipper", 0, ("Small doses, big feelings", "Nghe ít mà thấm"),
            ("Only {0} hours, but every one of them counted.", "Chỉ {0} giờ nhạc thôi, nhưng bài nào cũng là bài ruột."));
        B("Sipper", ("Selective", "Ít mà thấm"));

        // what
        T("Kids", 0, ("Doo doo doo parent", "Phụ huynh hệ Baby Shark"),
            ("{0} made your top list. We know who's really in charge of the playlist.", "{0} lọt top năm nay. Ai cầm điều khiển thì ai cũng biết rồi."));
        T("Kids", 1, ("The whole family goes doo doo doo", "Cả nhà đu đu đu"),
            ("Kids' songs ran your year, led by {0}. Doo doo doo doo doo doo.", "Nhạc thiếu nhi chiếm sóng cả năm, dẫn đầu là {0}. Đu đu đu đu đu đu."));
        B("Kids", ("Baby Shark parent", "Đu Baby Shark"));
        T("Summoner", 0, ("League runs in your blood", "Liên Minh chảy trong máu"),
            ("So much {0} your teammates know the lyrics.", "{0} vang lên nhiều tới mức đồng đội cũng thuộc lời."));
        T("Summoner", 1, ("Carrying the team with music", "Gánh team bằng nhạc"),
            ("League music took {1}% of your year. Lost? Blame the champ, not the playlist.", "Nhạc Liên Minh chiếm {1}% thời gian nghe. Thua là tại tướng, không tại nhạc."));
        B("Summoner", ("Summoner", "Triệu hồi sư"));
        T("Kpop", 0, ("K-pop runs in your blood", "K-pop chảy trong máu"),
            ("{0}% K-pop this year. Where's your lightstick?", "{0}% thời gian là K-pop. Lightstick đâu rồi?"));
        T("Kpop", 1, ("Stan till you drop", "Đu idol không biết mệt"),
            ("{0}% K-pop this year. Wallet thin, heart full.", "{0}% năm nay là K-pop. Ví thì mỏng, tim thì đầy."));
        B("Kpop", ("K-pop stan", "Đu idol"));
        T("VIndie", 0, ("Rain, coffee and indie", "Mưa, cà phê và nhạc indie"),
            ("{0} and the usual indie crowd. You definitely have a favourite café.", "{0} và mấy cái tên indie quen. Nghe là biết hay ngồi quán một mình."));
        T("VIndie", 1, ("Café corner soul", "Tâm hồn quán cà phê"),
            ("Vietnamese indie all year, led by {0}. Only missing the rain.", "Indie Việt cả năm, đứng đầu là {0}. Chỉ thiếu mỗi cơn mưa."));
        B("VIndie", ("Indie kid", "Hệ indie"));
        T("VRap", 0, ("Rap runs in your blood", "Rap Việt chảy trong máu"),
            ("{0} and the whole rap crew ran your headphones.", "{0} và cả hội rapper chiếm sóng tai nghe của bạn."));
        T("VRap", 1, ("Smoother flow than your ex", "Flow mượt hơn người yêu cũ"),
            ("Rap all year, led by {0}. You have flow now. Probably.", "Rap Việt cả năm, dẫn đầu là {0}. Nghe xong tự thấy mình có flow."));
        B("VRap", ("Rap head", "Hệ rap"));
        T("Bolero", 0, ("Bolero runs in your blood", "Bolero chảy trong máu"),
            ("{0}% bolero. You'd own a karaoke night.", "{0}% thời gian là bolero. Đi karaoke chắc khỏi chê."));
        T("Bolero", 1, ("Karaoke is home", "Tâm hồn bolero, karaoke là nhà"),
            ("{0}% bolero. Every microphone knows your voice.", "{0}% là bolero. Micro nào cũng nhận ra giọng bạn."));
        B("Bolero", ("Bolero heart", "Hệ bolero"));
        T("Sad", 0, ("Cry, play, repeat", "Buồn thì nghe nhạc, nghe nhạc lại buồn"),
            ("{0}% of your listening had 'miss', 'cry' or 'alone' in the title. You okay?", "{0}% thời gian là bài có chữ \"nhớ\", \"buồn\", \"cô đơn\". Ổn không đó?"));
        T("Sad", 1, ("Heartbreak, curated", "Thất tình cũng phải có gu"),
            ("Sad songs took {0}% of your year. Even crying needs a good playlist.", "Nhạc buồn chiếm {0}% năm nay. Ôm gối khóc cũng phải chọn bài."));
        B("Sad", ("Sad song club", "Hội nhạc buồn"));
        T("Remix", 0, ("Club in your bedroom", "Bay phòng không cần phòng"),
            ("{0}% remixes and bangers. Your neighbours know.", "{0}% là remix, vinahouse, EDM. Hàng xóm biết hết rồi."));
        T("Remix", 1, ("If it's not a remix, it's not music", "Không remix không phải nhạc"),
            ("{0}% remixes. The originals feel a bit slow, right?", "{0}% là bản remix. Bản gốc nghe hơi chậm đúng không?"));
        B("Remix", ("Party mode", "Dân bay"));
        T("SpedUp", 0, ("Life's short, play it at 1.25x", "Đời ngắn lắm, bật x1.25"),
            ("{0}% sped up or nightcore. One slow second is a second wasted.", "{0}% là bản sped up, nightcore. Chậm một giây là thấy phí."));
        T("SpedUp", 1, ("Faster than your ex moved on", "Nhanh như người yêu cũ trở mặt"),
            ("{0}% sped up. Normal speed is too slow for you.", "{0}% là bản sped up. Tốc độ bình thường quá chậm với bạn."));
        B("SpedUp", ("1.25x", "x1.25"));
        T("Lofi", 0, ("Lo-fi, long life", "Nhạc lofi lâu phai"),
            ("{0}% lo-fi and chill. Never gets old. The homework does.", "{0}% là lofi và chill. Nghe hoài không chán, bài tập thì vẫn chưa xong."));
        T("Lofi", 1, ("Lo-fi beats to procrastinate to", "Lofi để học, nhưng không học"),
            ("{0}% lo-fi and chill beats. So, how's that assignment going?", "{0}% là lofi. Bài tập làm tới đâu rồi?"));
        B("Lofi", ("Lo-fi", "Lofi lâu phai"));
        T("Christmas", 0, ("Every month is December", "Tháng nào cũng là tháng Mười Hai"),
            ("{0}% Christmas music. Mariah Carey says thank you.", "{0}% là nhạc Giáng sinh. Mariah Carey gửi lời cảm ơn."));
        B("Christmas", ("Year-round Christmas", "Noel quanh năm"));
        T("Anime", 0, ("Weeb with taste", "Wibu có gu"),
            ("{0}% J-pop and anime. You never skip an opening.", "{0}% là nhạc Nhật và anime. Opening nào cũng không bỏ."));
        B("Anime", ("Weeb", "Wibu có gu"));
        T("Ost", 0, ("Life's a movie, you're the lead", "Đời là phim, mình là vai chính"),
            ("{0}% soundtracks. You walk around like the credits are rolling.", "{0}% là nhạc phim. Đi đâu cũng như đang có nhạc nền."));
        B("Ost", ("Main character", "Vai chính"));
        T("Acoustic", 0, ("Unplugged and classy", "Mộc mạc mà sang"),
            ("{0}% acoustic, covers and live takes. Real voices only.", "{0}% là bản acoustic, cover, live. Thích nghe thật, hát thật."));
        B("Acoustic", ("Unplugged", "Hệ mộc"));

        T("AllRounder", 0, ("A bit of everything", "Gu mặn ngọt đủ cả"),
            ("Morning, noon or night, there's always music on.", "Sáng trưa chiều tối, lúc nào cũng có nhạc."));
        T("AllRounder", 1, ("Your taste has no rules", "Nghe gì cũng được, miễn là hay"),
            ("No box fits your taste, and that's the point.", "Không đóng khung được gu của bạn, thế mới hay."));
        B("AllRounder", ("All-rounder", "Đa năng"));
    }
}
