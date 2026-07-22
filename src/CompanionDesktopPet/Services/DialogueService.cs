namespace CompanionDesktopPet.Services;

public sealed class DialogueService
{
    private static readonly string[] Phrases =
    [
        "今天也很棒呀 ♡",
        "伸个懒腰吧，我陪着你",
        "喝一小口水，好不好？",
        "忙完这一点，就休息一下吧",
        "嘿嘿，被你发现我在发呆啦",
        "保持好心情，幸运会靠近你的",
        "别皱眉啦，慢慢来就好",
        "给你一颗小爱心 ♡"
    ];

    private int _lastPhraseIndex = -1;

    public string GetGreeting(DateTime localTime) => localTime.Hour switch
    {
        >= 5 and < 12 => "早上好呀，今天也一起加油 ♡",
        >= 12 and < 18 => "下午好，要记得喝水哦 ♡",
        >= 18 and < 24 => "晚上好，辛苦一天啦 ♡",
        _ => "这么晚还没睡呀？要照顾好自己哦"
    };

    public string GetNextPhrase(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        int index;
        do
        {
            index = random.Next(Phrases.Length);
        }
        while (index == _lastPhraseIndex);

        _lastPhraseIndex = index;
        return Phrases[index];
    }
}
