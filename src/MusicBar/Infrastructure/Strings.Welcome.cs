using System.Collections.Generic;

namespace MusicBar.Infrastructure;

public static partial class Strings
{
    /// <summary>The welcome tour, shown the first time MusicBar runs (and from Settings → About).</summary>
    static partial void AddWelcome(Dictionary<string, (string En, string Vi)> d)
    {
        d.Add("Wel_Window", ("Welcome to MusicBar", "Chào mừng đến với MusicBar"));
        d.Add("Wel_Hero", ("Let your taskbar sing.", "Cho taskbar hát đi."));
        d.Add("Wel_HeroSub", ("It has sat still for thirty years. Give it a minute: pick a few things, and the music does the rest. Whatever you pick shows on your taskbar right away.",
            "Thanh taskbar nằm im ba mươi năm rồi. Cho nó một phút: chọn vài thứ, phần còn lại để nhạc lo. Chọn gì, taskbar đổi ngay cái đó."));
        d.Add("Wel_Start", ("Let's go", "Bắt đầu"));
        d.Add("Wel_UseDefaults", ("Skip, keep it as it comes", "Bỏ qua, dùng thiết lập sẵn"));
        d.Add("Wel_Chip_Bar", ("A bar that dances", "Thanh nhảy theo nhạc"));
        d.Add("Wel_Chip_Lyrics", ("Karaoke lyrics", "Lời karaoke"));
        d.Add("Wel_Chip_Center", ("A control center", "Trung tâm điều khiển"));
        d.Add("Wel_Next", ("Next", "Tiếp"));
        d.Add("Wel_Back", ("Back", "Quay lại"));
        d.Add("Wel_SkipAll", ("Skip the tour", "Bỏ qua hướng dẫn"));
        d.Add("Wel_Later", ("Later: {0}", "Lần sau: {0}"));
        d.Add("Wel_Demo", ("Nothing is playing, so a demo song plays on your taskbar while you choose.", "Chưa có nhạc nên một bài mẫu đang phát trên taskbar để bạn xem thử."));
        d.Add("Wel_Close", ("Close (Esc)", "Đóng (Esc)"));

        d.Add("Wel_Style_Name", ("Look", "Kiểu thanh"));
        d.Add("Wel_Style_Title", ("Pick a look for the bar", "Chọn kiểu cho thanh"));
        d.Add("Wel_Style_Sub", ("It runs along the whole taskbar and takes its colors from the cover. Click one, then glance down at your taskbar.",
            "Thanh chạy dọc cả taskbar, đổi màu theo ảnh bìa. Bấm một kiểu rồi nhìn xuống taskbar."));
        d.Add("Wel_Style_Strength", ("Strength", "Độ đậm"));
        d.Add("Wel_Style_Where", ("Settings → Progress bar, with 30 looks, visualizers, the LED strip and skins", "Cài đặt → Thanh tiến trình, có 30 kiểu, visualizer, dải LED và skin"));

        d.Add("Wel_Bar_Name", ("Taskbar", "Taskbar"));
        d.Add("Wel_Bar_Title", ("Your taskbar", "Taskbar của bạn"));
        d.Add("Wel_Bar_Sub", ("See-through, and the bar lies right under the icons. Or keep a background behind them.",
            "Trong suốt thì thanh nằm ngay dưới các icon. Hoặc giữ một lớp nền phía sau."));
        d.Add("Wel_Bar_Clear", ("See-through taskbar", "Taskbar trong suốt"));
        d.Add("Wel_Bar_Clear_DescAutoHide", ("Your taskbar hides itself, so it keeps Windows' background: apps sit right behind it when it slides up.", "Taskbar của bạn đang tự ẩn nên vẫn giữ nền của Windows: lúc nó trồi lên, phía sau là cửa sổ ứng dụng."));
        d.Add("Wel_Bar_Clear_Desc", ("Built in, no TranslucentTB needed. Exit MusicBar and the taskbar is as it was.", "Có sẵn, không cần TranslucentTB. Thoát MusicBar là taskbar về như cũ."));
        d.Add("Wel_Bar_Avoid", ("Keep clear of the icons", "Né các icon"));
        d.Add("Wel_Bar_Avoid_Desc", ("The bar runs through the empty stretches only.", "Thanh chỉ chạy ở những khoảng trống."));
        d.Add("Wel_Bg_Clear", ("Clear", "Trong suốt"));
        d.Add("Wel_Bg_Windows", ("Like Windows", "Theo Windows"));
        d.Add("Wel_Bg_Song", ("Song's color", "Màu bài hát"));
        d.Add("Wel_Bg_Dark", ("Dark", "Tối"));
        d.Add("Wel_Bar_Where", ("Settings → Taskbar", "Cài đặt → Taskbar"));

        d.Add("Wel_Player_Name", ("Player", "Trình phát"));
        d.Add("Wel_Player_Title", ("The player by the tray", "Trình phát cạnh khay"));
        d.Add("Wel_Player_Sub", ("The cover, the buttons and the line being sung; or only the lyrics; or nothing but the bar.",
            "Ảnh bìa, nút và câu đang hát; hoặc chỉ lời; hoặc chỉ có thanh."));
        d.Add("Wel_Mode_Full", ("Player and lyrics", "Trình phát và lời"));
        d.Add("Wel_Mode_Lyrics", ("Only the lyrics", "Chỉ lời"));
        d.Add("Wel_Mode_Off", ("Just the bar", "Chỉ có thanh"));
        d.Add("Wel_Player_Buttons", ("Buttons", "Kiểu nút"));
        d.Add("Wel_Ctl_Glow", ("Glow", "Đĩa sáng"));
        d.Add("Wel_Ctl_Minimal", ("Minimal", "Tối giản"));
        d.Add("Wel_Ctl_Capsule", ("Capsule", "Viên kẹo"));
        d.Add("Wel_Ctl_Ring", ("Ring", "Vòng tiến trình"));
        d.Add("Wel_Ctl_Vinyl", ("Vinyl", "Đĩa than"));
        d.Add("Wel_Ctl_Keys", ("Cassette", "Phím cassette"));
        d.Add("Wel_Ctl_Bubbles", ("Bubbles", "Bong bóng"));
        d.Add("Wel_Player_Where", ("Settings → Player, or right-click the player", "Cài đặt → Trình phát, hoặc chuột phải lên trình phát"));

        d.Add("Wel_Lyrics_Name", ("Lyrics", "Lời bài hát"));
        d.Add("Wel_Lyrics_Title", ("Lyrics on the taskbar", "Lời trên taskbar"));
        d.Add("Wel_Lyrics_Sub", ("Synced from LRCLIB, colored in word by word as it's sung.", "Lấy từ LRCLIB, tô màu từng chữ theo lời hát."));
        d.Add("Wel_Lyrics_Show", ("Show the lyrics", "Hiện lời"));
        d.Add("Wel_Lyrics_Karaoke", ("Karaoke coloring", "Tô màu karaoke"));
        d.Add("Wel_Lyrics_Size", ("Size", "Cỡ chữ"));
        d.Add("Wel_Lyrics_Sample", ("Let your taskbar sing", "Cho taskbar hát đi"));
        d.Add("Wel_Lyrics_Where", ("Settings → Lyrics", "Cài đặt → Lời bài hát"));

        d.Add("Wel_Face_Name", ("Face", "Cái mặt"));
        d.Add("Wel_Face_Title", ("A little friend", "Một người bạn nhỏ"));
        d.Add("Wel_Face_Sub", ("It sings along, dozes off late at night and giggles when you poke it.", "Nó hát theo nhạc, ngủ gật lúc khuya và cười khi bị chọc."));
        d.Add("Wel_Face_Show", ("Show the face", "Hiện cái mặt"));
        d.Add("Wel_Face_Where", ("Settings → Face", "Cài đặt → Cái mặt"));

        d.Add("Wel_Habits_Name", ("Habits", "Thói quen"));
        d.Add("Wel_Habits_Title", ("A few habits", "Vài thói quen"));
        d.Add("Wel_Habits_Sub", ("How MusicBar fits into your day.", "MusicBar sống cùng máy bạn thế nào."));
        d.Add("Wel_Habit_Start", ("Start with Windows", "Khởi động cùng Windows"));
        d.Add("Wel_Habit_Idle", ("Step aside when no music plays", "Ẩn khi không có nhạc"));
        d.Add("Wel_Habit_Center", ("MusicBar's control center on the tray", "Trung tâm điều khiển MusicBar ở khay"));
        d.Add("Wel_Habit_Center_Desc", ("Notifications, quick settings, volume and speakers in one panel; Win + A opens it.", "Thông báo, cài đặt nhanh, âm lượng và loa trong một bảng; Win + A mở nó."));
        d.Add("Wel_Habit_Dot", ("A red dot for new notifications", "Chấm đỏ báo thông báo mới"));
        d.Add("Wel_Habits_Where", ("Settings → General and Taskbar", "Cài đặt → Chung và Taskbar"));

        d.Add("Wel_Done_Name", ("Done", "Xong"));
        d.Add("Wel_Done_Title", ("All set. Your taskbar sings now.", "Xong! Taskbar của bạn biết hát rồi."));
        d.Add("Wel_Skipped_Title", ("Set up as it comes", "Đã dùng thiết lập sẵn"));
        d.Add("Wel_Done_Sub", ("Anything can be changed again, any time:", "Muốn đổi gì, lúc nào cũng được:"));
        d.Add("Wel_Tip_Tray", ("The MusicBar icon in the tray", "Biểu tượng MusicBar ở khay hệ thống"));
        d.Add("Wel_Tip_Tray_Desc", ("Opens the settings. This tour is there too, under About.", "Mở cài đặt. Phần hướng dẫn này cũng ở đó, mục Giới thiệu."));
        d.Add("Wel_Tip_Right", ("Right-click the player", "Chuột phải lên trình phát"));
        d.Add("Wel_Tip_Right_Desc", ("Quick switches: the bar's look, the lyrics, the player to follow.", "Đổi nhanh kiểu thanh, lời, trình phát đang theo."));
        d.Add("Wel_Tip_Lyrics", ("Click the lyrics", "Bấm vào lời"));
        d.Add("Wel_Tip_Lyrics_Desc", ("The whole song: jump to any line, fix the timing, find other lyrics.", "Lời cả bài: nhảy tới dòng nào cũng được, chỉnh lệch, tìm lời khác."));
        d.Add("Wel_OpenSettings", ("Open the settings", "Mở cài đặt"));
        d.Add("Wel_Finish", ("Start listening", "Nghe nhạc thôi"));
    }
}
