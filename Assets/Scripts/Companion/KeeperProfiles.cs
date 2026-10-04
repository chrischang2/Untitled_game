using System.Collections.Generic;
using System.Linq;

namespace UntitledGame.Companion
{
    /// <summary>Something the player can learn about a shopkeeper (shown in the journal's People page).</summary>
    public class KeeperFact
    {
        public string id;          // "hometown", "family", "hobby", "secret", "like:茶", "dislike:咖啡"
        public string chinese;     // how the keeper says it (HSK 1-3 where possible)
        public string english;     // journal text (likes/dislikes are shown in Chinese + pinyin only)
        public int minLevel;       // friendship level needed before they'll share it
    }

    /// <summary>
    /// Who each shopkeeper is: personality, how they talk, what they like and dislike, facts about their life,
    /// topics they love, and questions they ask the player (by HSK difficulty). The topics are deliberately
    /// different (weather/boats, food/family, home/colours, animals/films, books/travel, sport/health,
    /// festivals/people) so getting to know everyone covers a wide range of HSK 1-3 vocabulary.
    /// First drafts: tweak freely.
    /// </summary>
    public class KeeperProfile
    {
        public string shopId;
        public string personality;   // for the keeper's prompt (English)
        public string speakingStyle; // for the keeper's prompt (English)
        public string[] likes;       // gift ids (= gift item hanzi)
        public string[] dislikes;
        public KeeperFact[] facts;   // life facts (likes/dislikes are generated from the arrays)
        public string[] topics;      // words that show interest in them (bonus friendship)
        public string[][] questions; // [0] = HSK 1, [1] = HSK 2, [2] = HSK 3
        public string likeReason;    // why they like their favourite gift (Chinese, short)
        public string dislikeReason;

        public IEnumerable<KeeperFact> AllFacts()
        {
            foreach (var f in facts) yield return f;
            foreach (var l in likes) yield return new KeeperFact { id = "like:" + l, chinese = $"我很喜欢{l}。", minLevel = 1 };
            foreach (var d in dislikes) yield return new KeeperFact { id = "dislike:" + d, chinese = $"我不喜欢{d}。", minLevel = 1 };
        }

        public KeeperFact Fact(string id) => AllFacts().FirstOrDefault(f => f.id == id);
    }

    public static class KeeperProfiles
    {
        public static readonly List<KeeperProfile> All = new List<KeeperProfile>
        {
            new KeeperProfile
            {
                shopId = "tackle",
                personality = "a gruff but kind retired fisherman, about sixty-five, who loves telling stories about the big fish that got away, " +
                              "always talks about the weather and boats, and acts tough but has a soft heart",
                speakingStyle = "short, blunt sentences; often starts with 嗯 or laughs 哈哈; proud of his fishing",
                likes = new[] { "茶", "帽子" }, dislikes = new[] { "咖啡" },
                likeReason = "天气热的时候，喝茶最好！", dislikeReason = "咖啡太苦了，我喝了晚上睡不着。",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我老家在北方的一个小城市。", english = "Comes from a small city in the north.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我喜欢看比赛，也喜欢爬山。", english = "Loves watching sports matches and climbing hills.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我儿子在北京当医生。", english = "His son is a doctor in Beijing.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "我一直想钓到一条很大的金枪鱼。", english = "His dream: to catch a giant bluefin tuna.", minLevel = 3 },
                },
                topics = new[] { "天气", "船", "鱼", "钓鱼", "比赛", "爬山", "儿子", "北京", "下雨" },
                questions = new[]
                {
                    new[] { "你喜欢钓鱼吗？", "今天天气怎么样？" },
                    new[] { "你钓到过什么鱼？", "你会游泳吗？" },
                    new[] { "你为什么喜欢钓鱼？", "你觉得住在海边怎么样？" },
                },
            },
            new KeeperProfile
            {
                shopId = "fish",
                personality = "a warm, chatty auntie, about fifty-five, who loves cooking, fusses over everyone like family, " +
                              "gives advice about food and health, and dances in the park every evening",
                speakingStyle = "lots of 哎呀 and 啊; calls the customer 孩子 (child); asks if they have eaten",
                likes = new[] { "花", "葡萄" }, dislikes = new[] { "糖" },
                likeReason = "花真漂亮，我要放在桌子上！", dislikeReason = "糖对身体不好，我不吃。",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我一直住在柳湾。", english = "Has lived in Willow Bay all her life.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我每天晚上在公园跳舞。", english = "Dances in the park every evening.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我有一个女儿，她是小学老师。", english = "Has a daughter who teaches primary school.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "我年轻的时候唱歌非常好听。", english = "She was a wonderful singer when she was young.", minLevel = 3 },
                },
                topics = new[] { "做饭", "菜", "吃饭", "好吃", "女儿", "跳舞", "公园", "身体" },
                questions = new[]
                {
                    new[] { "你吃饭了吗？", "你喜欢吃鱼吗？" },
                    new[] { "你今天累不累？", "你家有几个人？" },
                    new[] { "你会做中国菜吗？", "你觉得什么菜最好吃？" },
                },
            },
            new KeeperProfile
            {
                shopId = "furniture",
                personality = "a calm, precise, quiet carpenter, about forty-five, proud of his work, who measures everything, " +
                              "loves quiet music and coffee, and only opens up slowly",
                speakingStyle = "slow, polite and exact; likes numbers, sizes and colours; few words",
                likes = new[] { "咖啡", "手表" }, dislikes = new[] { "花" },
                likeReason = "我每天早上都要喝一杯咖啡。", dislikeReason = "我一看到花，身体就不舒服。",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我从南方来，十年前搬到了柳湾。", english = "Came from the south and moved to Willow Bay ten years ago.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我喜欢一边听音乐一边画画。", english = "Likes painting while listening to music.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我和妻子住在海边，我们有一只猫。", english = "Lives by the sea with his wife and their cat.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "我在给妻子做一把特别的椅子，别告诉她！", english = "He is secretly making a special chair for his wife.", minLevel = 3 },
                },
                topics = new[] { "音乐", "画", "椅子", "桌子", "颜色", "妻子", "房间", "咖啡" },
                questions = new[]
                {
                    new[] { "你喜欢什么颜色？", "你的家大吗？" },
                    new[] { "你喜欢听音乐吗？", "你的房间里有什么？" },
                    new[] { "你周末一般做什么？", "你觉得房间里放什么最好看？" },
                },
            },
            new KeeperProfile
            {
                shopId = "pet",
                personality = "a cheerful, excitable young woman, twenty-two, who adores animals (especially cats), loves films and taking photos, " +
                              "and always asks about Tangyuan",
                speakingStyle = "fast and bubbly; says 好可爱！ and 真的吗？ a lot",
                likes = new[] { "蛋糕", "糖", "西瓜" }, dislikes = new[] { "牛奶" },
                likeReason = "我最喜欢吃甜的东西了！", dislikeReason = "我不喝牛奶，喝了就不舒服。",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我是上海人，上大学的时候来到了柳湾。", english = "From Shanghai; came to Willow Bay for university.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我喜欢看电影和照相。", english = "Loves films and taking photos.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我有一个妹妹，她很喜欢狗。", english = "Has a little sister who loves dogs.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "我以后想开一个很大的动物医院。", english = "Dreams of opening a big animal hospital one day.", minLevel = 3 },
                },
                topics = new[] { "猫", "狗", "动物", "电影", "照片", "照相", "汤圆", "妹妹", "可爱" },
                questions = new[]
                {
                    new[] { "汤圆好吗？", "你有猫吗？" },
                    new[] { "你喜欢看电影吗？", "汤圆今天吃了什么？" },
                    new[] { "你为什么叫你的猫汤圆？", "你觉得什么动物最可爱？" },
                },
            },
            new KeeperProfile
            {
                shopId = "books",
                personality = "a gentle retired schoolteacher, about seventy, polite and patient, who loves history, stories and travel, " +
                              "and kindly helps learners say things correctly",
                speakingStyle = "polite (uses 您 about others), clear and slow; sometimes repeats the customer's sentence correctly",
                likes = new[] { "伞", "茶" }, dislikes = new[] { "帽子" },
                likeReason = "我喜欢下雨天去公园走走。", dislikeReason = "我觉得帽子不好看。",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我是北京人，在北京教了四十年书。", english = "From Beijing; taught there for forty years.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我喜欢看书，也喜欢下雨天在公园里走走。", english = "Loves reading and walking in the park on rainy days.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我丈夫也是老师，我们常常一起去旅游。", english = "Her husband is also a teacher; they often travel together.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "我年轻的时候自己写过一本书。", english = "She once wrote a book herself when she was young.", minLevel = 3 },
                },
                topics = new[] { "书", "看书", "历史", "故事", "旅游", "学习", "汉语", "北京", "老师" },
                questions = new[]
                {
                    new[] { "你是哪国人？", "你喜欢看书吗？" },
                    new[] { "你学汉语多长时间了？", "你去过北京吗？" },
                    new[] { "你为什么想学汉语？", "你觉得汉语难不难？" },
                },
            },
            new KeeperProfile
            {
                shopId = "gym",
                personality = "a loud, energetic fitness coach, about thirty-five, always cheerful, who counts everything out loud, " +
                              "shouts encouragement and cares a lot about healthy food and enough sleep",
                speakingStyle = "loud and short; says 加油！ and 很好！; counts 一、二、三！",
                likes = new[] { "香蕉", "牛奶" }, dislikes = new[] { "蛋糕", "糖" },
                likeReason = "运动以后吃香蕉，身体好！", dislikeReason = "太甜了，对身体不好！",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我在山里长大，小时候每天跑步去上学。", english = "Grew up in the hills and ran to school every day as a child.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我喜欢跑步、游泳和踢足球。", english = "Loves running, swimming and football.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我有一个哥哥，他是游泳运动员。", english = "His older brother is a competitive swimmer.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "其实……我很害怕狗。", english = "He is actually scared of dogs.", minLevel = 3 },
                },
                topics = new[] { "跑步", "游泳", "运动", "身体", "健康", "足球", "锻炼", "睡觉", "哥哥" },
                questions = new[]
                {
                    new[] { "你今天跑步了吗？", "你几点睡觉？" },
                    new[] { "你每天运动吗？", "你会游泳吗？" },
                    new[] { "你最喜欢什么运动？为什么？", "你觉得怎么样才能身体健康？" },
                },
            },
            new KeeperProfile
            {
                shopId = "colours",
                personality = "a cheerful young artist, about twenty-five, who loves colours, clothes and painting, " +
                              "notices what everyone is wearing and always has paint on his hands",
                speakingStyle = "excited and friendly; talks about colours all the time (红色、蓝色、黄色…) and says 太漂亮了！",
                likes = new[] { "花", "蛋糕" }, dislikes = new[] { "伞" },
                likeReason = "花的颜色太漂亮了！", dislikeReason = "我喜欢下雨，不要伞！",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我是南方人，我家在一个很大的城市。", english = "From a big city in the south.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我喜欢画画，也喜欢买新衣服。", english = "Loves painting and buying new clothes.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我爸爸妈妈都是老师，可是我想当画家。", english = "Both his parents are teachers, but he wants to be a painter.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "汤圆的小帽子是我自己做的！", english = "He made Tangyuan's little hats himself.", minLevel = 3 },
                },
                topics = new[] { "颜色", "红色", "蓝色", "黄色", "白色", "衣服", "画画", "漂亮" },
                questions = new[]
                {
                    new[] { "你喜欢什么颜色？", "你今天的衣服是什么颜色的？" },
                    new[] { "你喜欢画画吗？", "你的船是什么颜色的？" },
                    new[] { "你觉得汤圆戴帽子可爱吗？", "你为什么喜欢这个颜色？" },
                },
            },
            new KeeperProfile
            {
                shopId = "gifts",
                personality = "a sweet, talkative grandmother, about eighty, who has lived in the bay all her life, knows everyone's news " +
                              "and loves to tell the customer what the other shopkeepers like; a little hard of hearing, she sometimes asks people to say it again",
                speakingStyle = "warm and slow; calls the customer 孩子; sometimes says 什么？再说一遍！ when she didn't hear",
                likes = new[] { "面包", "葡萄" }, dislikes = new[] { "手表" },
                likeReason = "我早上最喜欢吃面包。", dislikeReason = "看手表，时间就走得太快了！",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我一直住在柳湾，这里的人我都认识。", english = "Has lived in Willow Bay all her life and knows everybody.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我喜欢唱歌，常常唱老歌。", english = "Loves singing old songs.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我有三个孩子，还有很多孩子的孩子！", english = "Has three children and lots of grandchildren.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "我和老王是小学同学。", english = "She and Old Wang went to primary school together.", minLevel = 3 },
                },
                topics = new[] { "唱歌", "孩子", "朋友", "礼物", "天气", "老王", "以前" },
                questions = new[]
                {
                    new[] { "你今天好吗？", "你多大了？" },
                    new[] { "你有很多朋友吗？", "你想给谁买礼物？" },
                    new[] { "你觉得柳湾最漂亮的地方是哪儿？", "你最好的朋友是谁？" },
                },
            },
            new KeeperProfile
            {
                shopId = "school",
                personality = "a strict but warm Chinese teacher, about fifty, who runs the HSK test centre, is always on time, " +
                              "cares a lot about good pronunciation and homework, and is secretly very proud of every student who passes",
                speakingStyle = "clear, slow and correct; says 很好！ and 再说一遍; sometimes asks the student to say a word again",
                likes = new[] { "手表", "咖啡" }, dislikes = new[] { "西瓜" },
                likeReason = "老师一定要知道时间，不能迟到！", dislikeReason = "西瓜太大了，吃起来桌子上都是水。",
                facts = new[]
                {
                    new KeeperFact { id = "hometown", chinese = "我是北京人，说的是普通话。", english = "From Beijing; speaks standard Mandarin.", minLevel = 0 },
                    new KeeperFact { id = "hobby", chinese = "我喜欢写字，也喜欢听音乐。", english = "Loves calligraphy and listening to music.", minLevel = 0 },
                    new KeeperFact { id = "family", chinese = "我有一个儿子，他在国外学习。", english = "Has a son who studies abroad.", minLevel = 1 },
                    new KeeperFact { id = "secret", chinese = "我年轻的时候，考试也常常考得不好！", english = "When she was young, she often failed her own exams!", minLevel = 3 },
                },
                topics = new[] { "汉语", "学习", "考试", "老师", "学生", "写字", "作业", "普通话", "音乐" },
                questions = new[]
                {
                    new[] { "你会说汉语吗？", "你今天学习了吗？" },
                    new[] { "你每天学习几个小时？", "你觉得汉语难吗？" },
                    new[] { "你为什么想学汉语？", "你觉得学汉语最难的是什么？" },
                },
            },
        };

        public static KeeperProfile For(string shopId) => All.FirstOrDefault(p => p.shopId == shopId);
    }
}
