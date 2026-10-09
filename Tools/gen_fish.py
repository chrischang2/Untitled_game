# Usage: py Tools/gen_fish.py  (rewrites the fish list in FishDatabase.cs and the books in Catalog.cs)
# Generates the FishDatabase.All list (80 species, 20 per region tier) and the fishing books.
# Each tier's gear limits: T0 line<=6 kg, <=24 m, baits worm/dough/shrimp/plain; T1 <=15 kg, <=38 m, +squid/crab;
# T2 <=40 kg, <=58 m, +live baitfish and glow lure; T3 <=80 kg, any distance.
# Per tier: 8 common, 6 uncommon, 4 rare, 2 legendary. The commonest (lowest rarity and difficulty) swims Mixed/Sinker
# and the rarest (legendary, highest difficulty) Mixed/Floater, so the balance checks hold.
import os, re
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

W, D, S_, P = 'bait_worm', 'bait_dough', 'bait_shrimp', 'none'
SQ, CR, FI, GL = 'bait_squid', 'bait_crab', 'bait_fish', 'bait_glow'

def F(id, name, hanzi, pinyin, rarity, mn, mx, times, diff, motion, dist, baits, line, body, fins, blurb,
       rain=False, needs=False, glow=None, model='S', shape=None):
    return dict(id=id, name=name, hanzi=hanzi, pinyin=pinyin, rarity=rarity, mn=mn, mx=mx, times=times, diff=diff, motion=motion,
                dist=dist, baits=baits, line=line, body=body, fins=fins, blurb=blurb, rain=rain, needs=needs, glow=glow, model=model, shape=shape)

C, U, R, L = 'Common', 'Uncommon', 'Rare', 'Legendary'
ANY = 'A'

tiers = {}
# ------------------------------------------------------------------ tier 0: Willow Bay (forest beach)
tiers[0] = [
    F('sardine', 'Sardine', '沙丁鱼', 'shā dīng yú', C, 0.03, 0.15, ANY, 15, 'Smooth', 0, [P, W, D], 1, '#9FB4C4', '#6F8798', "Small, silvery and everywhere. Everybody's first catch."),
    F('goby', 'Goby', '虾虎鱼', 'xiā hǔ yú', C, 0.02, 0.1, ANY, 10, 'Sinker', 0, [P, W, S_], 1, '#B59A6A', '#7E6A47', "A stubby little fish that sits on the sand under the dock.", shape=(0.85, 1.15, 1.1)),
    F('horsemackerel', 'Horse Mackerel', '竹荚鱼', 'zhú jiá yú', C, 0.1, 0.5, ANY, 25, 'Dart', 2, [W, D, S_], 1, '#8FA9A2', '#D8C25A', "Slim and quick, with a yellow streak. Comes in shoals in the morning.", shape=(1.15, 0.85, 0.85)),
    F('mullet', 'Mullet', '鲻鱼', 'zī yú', C, 0.3, 2, ANY, 30, 'Mixed', 3, [P, D], 2, '#7D8B94', '#56626A', "A long grey fish that jumps out of the water for no reason at all.", shape=(1.25, 0.9, 0.95)),
    F('croaker', 'Yellow Croaker', '小黄鱼', 'xiǎo huáng yú', C, 0.1, 0.6, 'MDE', 25, 'Mixed', 2, [W, S_], 1, '#E2C25A', '#C99A35', "Golden and chatty: it really does croak."),
    F('flounder', 'Flounder', '比目鱼', 'bǐ mù yú', C, 0.3, 2.5, ANY, 30, 'Sinker', 3, [W, S_], 2, '#A68B66', '#7E6648', "Flat as a plate, with both eyes on one side.", rain=True, shape=(1, 0.35, 1.6)),
    F('mackerel', 'Mackerel', '鲭鱼', 'qīng yú', C, 0.3, 1.2, 'MD', 40, 'Dart', 6, [W, S_], 2, '#4E7FA0', '#2F4E63', "Striped, fast, and travels in big shiny schools."),
    F('herring', 'Herring', '鲱鱼', 'fēi yú', C, 0.1, 0.4, 'MD', 20, 'Mixed', 1, [W, D], 1, '#A9BCC8', '#6C8494', "Silver and oily, and it travels in enormous shoals.", shape=(1.1, 0.9, 0.85)),
    F('pufferfish', 'Pufferfish', '河豚', 'hé tún', U, 0.2, 1.5, 'DE', 55, 'Floater', 5, [S_, CR], 2, '#D9C27A', '#A88F4C', "Puffs up into a spiky ball when it's cross.", shape=(0.8, 1.35, 1.35)),
    F('seabream', 'Sea Bream', '鲷鱼', 'diāo yú', U, 0.5, 4, 'MD', 50, 'Mixed', 10, [S_, CR, SQ], 5, '#E28A86', '#B9585A', "Pink and lucky: the fish for celebrations.", model='L'),
    F('seabass', 'Sea Bass', '鲈鱼', 'lú yú', U, 0.5, 6, 'MEN', 55, 'Mixed', 8, [S_, FI], 6, '#8E9EA6', '#5D6B72', "Hunts along the shore when the light is low.", rain=True, model='L'),
    F('hairtail', 'Hairtail', '带鱼', 'dài yú', U, 0.3, 2, 'EN', 60, 'Dart', 14, [SQ, FI], 3, '#D7DDE3', '#A9B3BC', "Long, thin and shiny like a silver ribbon.", shape=(2.2, 0.7, 0.45)),
    F('cuttlefish', 'Cuttlefish', '墨鱼', 'mò yú', U, 0.3, 2, 'EN', 50, 'Floater', 8, [S_], 3, '#C9A98C', '#8C6A55', "Squirts ink when it's startled. Mind your shirt.", shape=(0.9, 0.8, 1.2)),
    F('redgurnard', 'Red Gurnard', '红娘鱼', 'hóng niáng yú', U, 0.3, 1.5, 'D', 45, 'Sinker', 9, [W, S_], 3, '#D8604E', '#A8463A', "Walks along the seabed on little finger-like fins."),
    F('blackporgy', 'Black Porgy', '黑鲷', 'hēi diāo', R, 1, 4, 'EN', 66, 'Mixed', 12, [W, S_], 6, '#4A4E55', '#2E3136', "A dark, clever bream that steals bait from careless anglers.", rain=True, model='L'),
    F('turbot', 'Turbot', '多宝鱼', 'duō bǎo yú', R, 1, 8, 'DE', 62, 'Sinker', 16, [W, S_], 6, '#9A8468', '#6E5C46', "A round flatfish that hides under the sand. Chefs love it.", model='L', shape=(1, 0.35, 1.5)),
    F('johndory', 'John Dory', '海鲂', 'hǎi fáng', R, 0.5, 3, 'D', 64, 'Floater', 15, [S_], 5, '#C9B27A', '#8E7A4E', "Thin and golden, with a dark thumbprint on each side.", shape=(0.8, 1.4, 0.5)),
    F('yellowtail', 'Yellowtail', '鰤鱼', 'shī yú', R, 2, 10, 'M', 68, 'Dart', 20, [S_, P], 6, '#7E98A6', '#E3C24A', "Fast and strong: a fish that really pulls back.", model='L'),
    F('spottedbass', 'Seven-Star Bass', '七星鲈', 'qī xīng lú', L, 3, 15, 'E', 75, 'Dart', 20, [S_], 6, '#8FA0A6', '#3E4A50', "A great bass with seven dark stars along its side.", model='L'),
    F('willowspirit', 'Willow Spirit Fish', '柳仙鱼', 'liǔ xiān yú', L, 3, 12, 'N', 78, 'Mixed', 22, [D, W], 6, '#B9D99A', '#7FAF64', "A pale green fish said to be the spirit of the old willow tree.", glow='#9CD47A', model='L'),
]
# ------------------------------------------------------------------ tier 1: Golden Sand Bay (desert coast, warm sea)
tiers[1] = [
    F('clownfish', 'Clownfish', '小丑鱼', 'xiǎo chǒu yú', C, 0.05, 0.25, ANY, 20, 'Mixed', 0, [P, W, S_], 1, '#F08A3C', '#F7F2EA', "Orange and white, and lives in a stinging anemone as if it's nothing."),
    F('butterflyfish', 'Butterflyfish', '蝴蝶鱼', 'hú dié yú', C, 0.1, 0.4, 'D', 28, 'Floater', 2, [W, S_], 1, '#F2D24A', '#3A3A40', "Flat, yellow and flutters between the rocks.", shape=(0.8, 1.3, 0.5)),
    F('sandsmelt', 'Sand Smelt', '沙钻鱼', 'shā zuàn yú', C, 0.05, 0.3, 'MD', 25, 'Dart', 1, [W], 1, '#E3D3AC', '#B9A57A', "Dives into the sand when anything comes near.", shape=(1.3, 0.75, 0.75)),
    F('bluetang', 'Blue Tang', '蓝倒吊', 'lán dào diào', C, 0.2, 0.6, 'D', 32, 'Mixed', 4, [S_], 2, '#3E6FD0', '#F2C94C', "Bright blue with a yellow tail, and a bit forgetful.", shape=(0.9, 1.2, 0.6)),
    F('rabbitfish', 'Rabbitfish', '篮子鱼', 'lán zi yú', C, 0.3, 1.2, 'DE', 34, 'Mixed', 3, [D, W], 2, '#B7A77C', '#8A7A52', "Nibbles seaweed all day like a rabbit nibbles grass."),
    F('goatfish', 'Yellow Goatfish', '黄羊鱼', 'huáng yáng yú', C, 0.2, 0.9, 'D', 30, 'Sinker', 3, [W, S_], 2, '#E8C35A', '#C9963A', "Feels for food in the sand with two whiskers under its chin."),
    F('sweetlips', 'Sweetlips', '胡椒鲷', 'hú jiāo diāo', C, 0.5, 3, 'E', 38, 'Sinker', 6, [S_, CR], 6, '#D9D2B8', '#3A3A40', "Spotty, with big soft lips. It looks like it's about to kiss you.", model='L'),
    F('flyingfish', 'Flying Fish', '飞鱼', 'fēi yú', C, 0.2, 0.6, 'MD', 36, 'Dart', 8, [W, S_], 2, '#6C8FC4', '#B9D2EC', "Glides over the waves on wing-like fins.", shape=(1.2, 0.8, 1.3)),
    F('angelfish', 'Angelfish', '神仙鱼', 'shén xiān yú', U, 0.3, 1.5, 'D', 48, 'Floater', 6, [S_], 3, '#3F62B8', '#F2D24A', "Elegant stripes and long fins. It knows it's beautiful.", shape=(0.8, 1.4, 0.5)),
    F('parrotfish', 'Parrotfish', '鹦嘴鱼', 'yīng zuǐ yú', U, 1, 5, 'D', 55, 'Mixed', 10, [CR, S_], 6, '#4FB39A', '#E06A8C', "Crunches coral with its beak and makes white sand.", model='L'),
    F('lionfish', 'Lionfish', '狮子鱼', 'shī zi yú', U, 0.3, 1.2, 'EN', 58, 'Floater', 8, [S_, SQ], 3, '#C9503E', '#F2E6D2', "Covered in stripy spines like a lion's mane. Don't touch!"),
    F('octopus', 'Octopus', '章鱼', 'zhāng yú', U, 1, 8, 'EN', 65, 'Sinker', 6, [CR], 6, '#B5655A', '#8B4740', "Clever, shy and very good at hiding.", model='L', shape=(0.7, 1.3, 1.3)),
    F('triggerfish', 'Triggerfish', '炮弹鱼', 'pào dàn yú', U, 1, 4, 'D', 60, 'Mixed', 12, [CR, SQ], 6, '#5E6E8A', '#E3B23C', "Shaped like a cannonball and just as tough.", model='L'),
    F('stingray', 'Stingray', '魟鱼', 'hóng yú', U, 3, 20, 'EN', 62, 'Sinker', 14, [CR, SQ], 15, '#8A7A62', '#5E5240', "Glides over the sand like a big flat kite.", model='L', shape=(0.8, 0.3, 1.8)),
    F('conger', 'Conger Eel', '海鳗', 'hǎi mán', R, 1, 12, 'N', 70, 'Smooth', 12, [SQ, FI], 15, '#5B5E52', '#3E4038', "Lives in rocky holes and comes out on rainy nights.", rain=True, model='L', shape=(2, 0.6, 0.6)),
    F('grouper', 'Grouper', '石斑鱼', 'shí bān yú', R, 2, 25, 'DE', 72, 'Sinker', 18, [CR, FI], 15, '#8B6A4A', '#5E4632', "A heavy, spotty rock-dweller with a huge mouth.", model='L', shape=(0.95, 1.15, 1.15)),
    F('skipjack', 'Skipjack Tuna', '鲣鱼', 'jiān yú', R, 2, 8, 'MD', 75, 'Dart', 30, [FI, SQ], 15, '#3F5E86', '#9FB3C9', "A little tuna that never stops swimming.", model='L'),
    F('sailfish', 'Sailfish', '旗鱼', 'qí yú', R, 20, 60, 'D', 85, 'Dart', 34, [FI, SQ], 15, '#2E5C9A', '#6FA0D8', "The fastest fish in the sea, with a sail on its back.", model='L', shape=(1.4, 1.2, 0.7)),
    F('shark', 'Blue Shark', '鲨鱼', 'shā yú', L, 30, 120, 'EN', 88, 'Mixed', 36, [SQ, FI], 15, '#4F78A8', '#C9D6E3', "Long, blue and elegant. Mei pretends she isn't scared.", model='L', shape=(1.4, 0.9, 0.9)),
    F('goldendragon', 'Golden Sand Dragon', '沙漠金龙', 'shā mò jīn lóng', L, 10, 40, 'D', 92, 'Mixed', 36, [SQ, CR], 15, '#E8B83A', '#C98A2A', "A gleaming golden fish that rises only when the desert sun is highest.", glow='#E8B83A', model='L', shape=(1.5, 0.9, 0.8)),
]
# ------------------------------------------------------------------ tier 2: Snow Bay (snowy coast, icy sea)
tiers[2] = [
    F('capelin', 'Capelin', '毛鳞鱼', 'máo lín yú', C, 0.02, 0.08, 'MD', 20, 'Mixed', 0, [P, W], 1, '#A8BAC6', '#6E8494', "Tiny silver fish that arrive in their millions when the ice melts."),
    F('arcticcod', 'Arctic Cod', '北极鳕', 'běi jí xuě', C, 0.1, 0.6, ANY, 28, 'Mixed', 2, [W, S_], 2, '#B7B09A', '#857E6A', "A small cod that hides in cracks under the sea ice."),
    F('icefish', 'Icefish', '冰鱼', 'bīng yú', C, 0.05, 0.3, 'MN', 30, 'Floater', 1, [W], 1, '#DDEAF2', '#B6CEDD', "Almost see-through: it has no red blood at all.", shape=(1.2, 0.8, 0.7)),
    F('pollock', 'Pollock', '狭鳕', 'xiá xuě', C, 0.5, 3, 'DE', 32, 'Dart', 4, [S_, SQ], 6, '#7F8E8A', '#556460', "Swims in big grey crowds. Everyone's favourite fish fingers."),
    F('saury', 'Pacific Saury', '秋刀鱼', 'qiū dāo yú', C, 0.1, 0.2, 'EN', 34, 'Dart', 3, [W, S_], 1, '#6F8FAE', '#C9D3DC', "A slim blade of a fish; grilled, it tastes of autumn.", shape=(1.6, 0.7, 0.6)),
    F('haddock', 'Haddock', '黑线鳕', 'hēi xiàn xuě', C, 0.5, 3, 'D', 36, 'Sinker', 5, [W, SQ], 6, '#9A9C96', '#3E4146', "A cod with a black line down its side and a thumbprint on its shoulder."),
    F('char', 'Arctic Char', '红点鲑', 'hóng diǎn guī', C, 0.5, 4, 'ME', 38, 'Mixed', 6, [W, S_, FI], 6, '#5E7A6A', '#D8604E', "Red-bellied and spotty, from the coldest water of all."),
    F('cod', 'Atlantic Cod', '鳕鱼', 'xuě yú', C, 1, 10, ANY, 40, 'Sinker', 8, [SQ, FI], 15, '#A79C7E', '#7A7058', "A big, calm fish with a little beard under its chin.", model='L'),
    F('grayling', 'Grayling', '茴鱼', 'huí yú', U, 0.3, 2, 'M', 52, 'Dart', 7, [W, FI], 3, '#8C96A8', '#7A5FA8', "Carries a tall purple fin like a flag."),
    F('lumpfish', 'Lumpfish', '圆鳍鱼', 'yuán qí yú', U, 0.5, 5, 'D', 50, 'Floater', 10, [S_, CR], 6, '#6F8A5E', '#4E6640', "Round and bumpy; it sticks itself to rocks with a sucker.", shape=(0.8, 1.3, 1.2)),
    F('salmon', 'Salmon', '三文鱼', 'sān wén yú', U, 2, 15, 'ME', 55, 'Dart', 12, [FI, S_], 15, '#9AA6B0', '#E58E6C', "Strong, silver and always heading home.", rain=True, model='L'),
    F('sablefish', 'Sablefish', '银鳕鱼', 'yín xuě yú', U, 1, 6, 'N', 58, 'Mixed', 22, [FI, SQ], 15, '#4A4E58', '#2E3138', "Black as night and soft as butter.", model='L'),
    F('halibut', 'Halibut', '大比目鱼', 'dà bǐ mù yú', U, 5, 60, 'D', 60, 'Sinker', 20, [FI, SQ], 40, '#7A6E5E', '#544A3E', "A flatfish as big as a door.", model='L', shape=(1, 0.35, 1.6)),
    F('wolffish', 'Wolffish', '狼鱼', 'láng yú', U, 2, 15, 'N', 62, 'Sinker', 16, [CR, SQ], 15, '#6E7480', '#4A4E58', "Big teeth and a grumpy face, but a gentle heart.", model='L', shape=(1.6, 0.8, 0.8)),
    F('icepike', 'Ice Pike', '冰梭鱼', 'bīng suō yú', R, 3, 12, 'EN', 76, 'Dart', 30, [FI, SQ], 15, '#C6D8E4', '#7FA2BC', "A spear of a fish that hunts beneath the ice.", model='L', shape=(1.8, 0.7, 0.7)),
    F('auroratrout', 'Aurora Trout', '极光鳟', 'jí guāng zūn', R, 2, 9, 'N', 78, 'Mixed', 26, [FI, S_], 15, '#5FC7A8', '#8A6AD8', "Its scales shimmer like the northern lights.", glow='#6FE0C0', model='L'),
    F('greenlandshark', 'Greenland Shark', '格陵兰鲨', 'gé líng lán shā', R, 50, 200, 'N', 80, 'Sinker', 50, [FI, SQ], 40, '#5A6068', '#3E434A', "A slow, sleepy shark that may be four hundred years old.", model='L', shape=(1.5, 0.9, 0.9)),
    F('narwhal', 'Narwhal', '独角鲸', 'dú jiǎo jīng', R, 40, 120, 'D', 82, 'Floater', 45, [FI], 40, '#8A96A4', '#5E6874', "Not really a fish, but nobody told it. Look at that tusk!", model='L', shape=(1.7, 0.9, 0.9)),
    F('tuna', 'Bluefin Tuna', '金枪鱼', 'jīn qiāng yú', L, 60, 250, 'MD', 92, 'Mixed', 55, [FI], 40, '#273F66', '#D9B44A', "The king of the sea. Old Wang has dreamed of one for years.", model='L'),
    F('oarfish', 'Oarfish', '皇带鱼', 'huáng dài yú', L, 40, 200, 'N', 95, 'Floater', 55, [GL], 40, '#E3E7EE', '#E0604E', "A silver giant from the deep, said to rise only on dark nights.", needs=True, glow='#4D73D9', model='L', shape=(2.6, 0.8, 0.4)),
]
# ------------------------------------------------------------------ tier 3: Mars (a red planet with a purple sea)
tiers[3] = [
    F('dustminnow', 'Dust Minnow', '红沙鱼', 'hóng shā yú', C, 0.05, 0.2, ANY, 22, 'Mixed', 0, [P, W, D], 1, '#C8603A', '#8E3E26', "Tiny rust-red fish that swim in clouds like Martian dust."),
    F('irongoby', 'Iron Goby', '铁头鱼', 'tiě tóu yú', C, 0.2, 1, ANY, 26, 'Sinker', 2, [W, CR], 2, '#7A6E6A', '#4E4644', "Its head is as hard as a Martian rock.", shape=(0.9, 1.1, 1.1)),
    F('starsardine', 'Star Sardine', '星星鱼', 'xīng xing yú', C, 0.05, 0.3, 'N', 28, 'Dart', 1, [P, S_], 1, '#D8DCF2', '#9EA6D8', "Twinkles at night, so you can find it in the dark.", glow='#E8ECFF'),
    F('volcanocarp', 'Volcano Carp', '火山鲤', 'huǒ shān lǐ', C, 0.5, 3, 'D', 30, 'Sinker', 2, [D, W], 3, '#B5452E', '#E8853A', "Warm to the touch: it lives near hot springs under the sea."),
    F('purplepuff', 'Purple Puff', '紫气球鱼', 'zǐ qì qiú yú', C, 0.2, 1, 'DE', 32, 'Floater', 3, [S_], 2, '#9A5AC8', '#D8A8F2', "Floats like a balloon in the purple sea.", shape=(0.85, 1.3, 1.3)),
    F('moonfish', 'Moonfish', '月亮鱼', 'yuè liang yú', C, 0.3, 1.5, 'N', 34, 'Mixed', 4, [S_, SQ], 3, '#E2E2EE', '#B4B4CC', "Round and pale; it glows softly like a little moon.", glow='#D8D8FF', shape=(0.8, 1.3, 0.5)),
    F('robotfish', 'Robot Fish', '机器鱼', 'jī qì yú', C, 0.5, 2, ANY, 36, 'Mixed', 5, [P, W], 6, '#A8B2BC', '#E8743B', "Nobody knows who built it. It seems quite happy.", shape=(1.1, 0.9, 0.9)),
    F('rocketfish', 'Rocket Fish', '火箭鱼', 'huǒ jiàn yú', C, 0.3, 1.5, 'MD', 40, 'Dart', 7, [S_, FI], 6, '#E8E4DA', '#D9534A', "Shoots off so fast it leaves a trail of bubbles.", shape=(1.4, 0.8, 0.8)),
    F('crystalfish', 'Crystal Fish', '水晶鱼', 'shuǐ jīng yú', U, 0.5, 3, 'D', 52, 'Floater', 10, [S_, CR], 6, '#C8A8F2', '#8E6AD8', "See-through like glass, with a purple heart inside.", glow='#B48CFF'),
    F('meteoreel', 'Meteor Eel', '流星鳗', 'liú xīng mán', U, 1, 6, 'N', 55, 'Smooth', 12, [SQ, FI], 15, '#3E4A7A', '#6FE0F0', "Streaks across the sea at night like a shooting star.", glow='#6FE0F0', model='L', shape=(2, 0.6, 0.6)),
    F('twohead', 'Two-Headed Trout', '双头鱼', 'shuāng tóu yú', U, 1, 5, 'D', 56, 'Mixed', 9, [W, S_], 6, '#7AA06A', '#4E7A44', "One head eats while the other one keeps watch.", model='L'),
    F('sunfish', 'Solar Sunfish', '太阳鱼', 'tài yáng yú', U, 3, 20, 'D', 58, 'Floater', 18, [SQ, FI], 15, '#F2A43A', '#E8743B', "Soaks up the far-away sun and shines it back.", glow='#FFB84A', model='L', shape=(0.6, 1.4, 0.5)),
    F('alienoctopus', 'Alien Octopus', '外星章鱼', 'wài xīng zhāng yú', U, 2, 12, 'EN', 60, 'Sinker', 14, [CR, FI], 15, '#6AC86A', '#3E8A4E', "Green, with nine arms. It waves with all of them.", model='L', shape=(0.7, 1.3, 1.3)),
    F('gravitygrouper', 'Gravity Grouper', '太空石斑', 'tài kōng shí bān', U, 5, 30, ANY, 62, 'Sinker', 20, [CR, FI], 40, '#5E5A8A', '#3E3A64', "So heavy it bends the light around it. Probably.", model='L', shape=(0.95, 1.15, 1.15)),
    F('mooneel', 'Moon Eel', '月亮鳗', 'yuè liang mán', R, 4, 20, 'N', 76, 'Smooth', 30, [SQ, GL], 40, '#2E3E6E', '#8AB4FF', "A long blue eel that only comes up when both moons are out.", glow='#8AB4FF', model='L', shape=(2.2, 0.6, 0.6)),
    F('nebula', 'Nebula Angler', '星云鱼', 'xīng yún yú', R, 5, 25, 'N', 78, 'Floater', 35, [GL, FI], 40, '#4A2E6E', '#F27AC8', "Carries a lantern on its head as bright as a nebula.", glow='#FF7AD0', model='L', shape=(0.9, 1.1, 1.1)),
    F('galaxymarlin', 'Galaxy Marlin', '银河旗鱼', 'yín hé qí yú', R, 30, 120, 'D', 80, 'Dart', 45, [FI, SQ], 80, '#3A2E7A', '#B48CFF', "Its sail is full of stars.", glow='#9A7AFF', model='L', shape=(1.4, 1.2, 0.7)),
    F('whaleshark', 'Space Whale Shark', '太空鲸鲨', 'tài kōng jīng shā', R, 80, 300, 'E', 82, 'Mixed', 60, [FI, GL], 80, '#4E5A7A', '#E8ECF2', "Gentle and enormous, with spots like a map of the stars.", model='L', shape=(1.6, 0.9, 1.0)),
    F('leviathan', 'Martian Leviathan', '火星巨鱼', 'huǒ xīng jù yú', L, 120, 400, 'N', 94, 'Floater', 80, [GL], 80, '#8E3426', '#E8743B', "The oldest thing on Mars. Its back looks like a red mountain range.", needs=True, glow='#FF5A3A', model='L', shape=(1.8, 1.0, 1.0)),
    F('starkoi', 'Golden Star Koi', '金星锦鲤', 'jīn xīng jǐn lǐ', L, 20, 80, 'D', 97, 'Mixed', 70, [FI, SQ], 80, '#F2C04A', '#F7F2EA', "Said to have swum here from a distant star. It brings luck.", glow='#FFD45A', model='L', shape=(1.2, 1.0, 0.8)),
]

TIMES = {'M': 'TimeWindow.Morning', 'D': 'TimeWindow.Day', 'E': 'TimeWindow.Evening', 'N': 'TimeWindow.Night'}
MOTION_BAR = {'Smooth': 0.3, 'Sinker': 0.28, 'Mixed': 0.26, 'Floater': 0.24, 'Dart': 0.24}

def num(x):
    s = f"{x:g}"
    return s + 'f'

def emit(f):
    parts = [f'id = "{f["id"]}"', f'name = "{f["name"]}"', f'hanzi = "{f["hanzi"]}"', f'pinyin = "{f["pinyin"]}"', f'rarity = Rarity.{f["rarity"]}',
             f'minWeight = {num(f["mn"])}', f'maxWeight = {num(f["mx"])}']
    if f['times'] != 'A':
        parts.append('times = ' + ' | '.join(TIMES[c] for c in f['times']))
    if f['rain']:
        parts.append('likesRain = true')
    parts += [f'difficulty = {f["diff"]}', f'motion = FishMotion.{f["motion"]}', f'barSize = {num(MOTION_BAR[f["motion"]])}', f'minDistance = {num(f["dist"])}']
    baits = ', '.join('PlainHook' if b == 'none' else f'"{b}"' for b in f['baits'])
    parts.append(f'baits = new[] {{ {baits} }}')
    if f['needs']:
        parts.append('needsBait = true')
    parts += [f'lineKg = {num(f["line"])}', f'body = C("{f["body"]}")', f'fins = C("{f["fins"]}")']
    if f['glow']:
        parts.append(f'glow = C("{f["glow"]}")')
    if f['model'] == 'L':
        parts.append('model = CatchModel.LargeFish')
    if f['shape']:
        x, y, z = f['shape']
        parts.append(f'shape = new Vector3({num(x)}, {num(y)}, {num(z)})')
    parts.append(f'blurb = "{f["blurb"]}"')
    # wrap at ~150 chars
    lines, cur = [], '            new FishSpecies { '
    for i, p in enumerate(parts):
        piece = p + (', ' if i < len(parts) - 1 else ' },')
        if len(cur) + len(piece) > 165:
            lines.append(cur.rstrip())
            cur = '                ' + piece
        else:
            cur += piece
    lines.append(cur)
    return '\n'.join(lines)

HEAD = {0: '// ---- tier 0: Willow Bay (forest beach)', 1: '// ---- tier 1: Golden Sand Bay (desert coast, warm sea)',
        2: '// ---- tier 2: Snow Bay (snowy coast, icy sea)', 3: '// ---- tier 3: Mars (a red planet with a purple sea)'}

# sanity checks
LIMITS = {0: (6, 24, {W, D, S_, P}), 1: (15, 38, {W, D, S_, P, SQ, CR}), 2: (40, 58, {W, D, S_, P, SQ, CR, FI, GL}), 3: (80, 999, {W, D, S_, P, SQ, CR, FI, GL})}
ids = set()
for t, fl in tiers.items():
    assert len(fl) == 20, (t, len(fl))
    counts = {r: sum(1 for f in fl if f['rarity'] == r) for r in (C, U, R, L)}
    assert counts == {C: 8, U: 6, R: 4, L: 2}, (t, counts)
    line, dist, baits = LIMITS[t]
    for f in fl:
        assert f['id'] not in ids, f['id']; ids.add(f['id'])
        assert f['line'] <= line and f['dist'] <= dist, (f['id'], f['line'], f['dist'])
        assert not f['needs'] or set(f['baits']) <= baits, (f['id'], f['baits'])  # liked baits only boost bites
    commonest = min(fl, key=lambda f: (['Common', 'Uncommon', 'Rare', 'Legendary'].index(f['rarity']), f['diff']))
    rarest = max(fl, key=lambda f: (['Common', 'Uncommon', 'Rare', 'Legendary'].index(f['rarity']), f['diff']))
    assert commonest['motion'] in ('Mixed', 'Sinker'), (t, commonest['id'])
    assert rarest['motion'] in ('Mixed', 'Floater', 'Dart'), (t, rarest['id'])
    # within-tier dock reach: several commons from the dock (<= 10 m)
    assert sum(1 for f in fl if f['dist'] <= 10) >= 8, t
    print(t, 'commonest', commonest['id'], 'rarest', rarest['id'])

body = []
for t in range(4):
    body.append('            ' + HEAD[t])
    body += [emit(f) for f in tiers[t]]
    body.append('')
code = '\n'.join(body).rstrip() + '\n'

p = os.path.join(ROOT, 'Assets', 'Scripts', 'Fishing', 'FishDatabase.cs')
s = open(p, encoding='utf-8').read()
i = s.index('        public static readonly List<FishSpecies> All = new List<FishSpecies>\n        {') if '\r\n' not in s else None
s = s.replace('\r\n', '\n')
start = s.index('        public static readonly List<FishSpecies> All = new List<FishSpecies>\n        {\n') + len('        public static readonly List<FishSpecies> All = new List<FishSpecies>\n        {\n')
end = s.index('\n        };\n', start)
s = s[:start] + code + s[end:]
s = s.replace('''    /// <summary>
    /// One kind of sea creature.''', '''    /// <summary>
    /// One kind of sea creature (80 of them: 20 for each region's sea, from Willow Bay's sardines to Mars's star koi).''')
open(p, 'w', encoding='utf-8', newline='\r\n').write(s)

# ------------------------------------------------------------------ books
books = [
    ('book_basics', 0, 0, 'Fishing for Beginners', '钓鱼入门', 40, ['croaker', 'flounder', 'mackerel', 'herring', 'pufferfish', 'redgurnard'], 'The fish you meet first, and how to catch them.'),
    ('book_shore', 0, 0, 'Fish of the Shore', '海边的鱼', 150, ['seabream', 'seabass', 'hairtail', 'cuttlefish', 'blackporgy'], 'Bigger fish along the shore, for a stronger line.'),
    ('book_bay', 0, 1, 'Secrets of Willow Bay', '柳湾的秘密', 260, ['turbot', 'johndory', 'yellowtail', 'spottedbass', 'willowspirit'], "The bay's rare fish, and the legend of the willow spirit."),
    ('book_rocks', 1, 1, 'Fish under the Rocks', '石头下的鱼', 300, ['octopus', 'conger', 'grouper', 'sweetlips', 'goatfish', 'triggerfish', 'stingray'], 'What hides among the warm rocks of Golden Sand Bay.'),
    ('book_reef', 1, 1, 'Fish of the Coral Reef', '珊瑚里的鱼', 360, ['clownfish', 'butterflyfish', 'bluetang', 'angelfish', 'parrotfish', 'lionfish', 'rabbitfish'], 'The bright little fish of the coral reef.'),
    ('book_warm', 1, 2, 'Fish of the Warm Sea', '热海的鱼', 450, ['sandsmelt', 'flyingfish', 'skipjack', 'sailfish', 'shark', 'goldendragon'], 'Fast fish of the warm open sea, and the golden sand dragon.'),
    ('book_open', 2, 2, 'Fish of the Open Sea', '远海的鱼', 500, ['tuna', 'halibut', 'sablefish', 'greenlandshark', 'narwhal', 'pollock'], 'Giants of the cold open sea. You will need the boat.'),
    ('book_ice', 2, 2, 'Fish under the Ice', '冰海的鱼', 600, ['capelin', 'icefish', 'arcticcod', 'char', 'grayling', 'icepike', 'auroratrout'], 'Fish that love the coldest water.'),
    ('book_north', 2, 3, 'Fish of the North', '北方的鱼', 700, ['cod', 'haddock', 'saury', 'salmon', 'lumpfish', 'wolffish', 'oarfish'], 'Northern favourites, and the oarfish of the deep.'),
    ('book_legends', 3, 3, 'Fish of Mars', '火星的鱼', 900, ['dustminnow', 'volcanocarp', 'irongoby', 'purplepuff', 'rocketfish', 'robotfish', 'moonfish'], 'What swims in the purple sea of Mars.'),
    ('book_space', 3, 3, 'Fish from Space', '太空的鱼', 1100, ['starsardine', 'meteoreel', 'crystalfish', 'twohead', 'sunfish', 'alienoctopus', 'gravitygrouper'], 'Strange fish that fell from the stars.'),
    ('book_stars', 3, 4, 'Legends of the Stars', '星星的传说', 1400, ['galaxymarlin', 'whaleshark', 'nebula', 'mooneel', 'leviathan', 'starkoi'], "The rarest fish in the universe. Bring your best line."),
]
taught = [i for b in books for i in b[6]]
starters = ['sardine', 'goby', 'horsemackerel', 'mullet']
assert sorted(taught + starters) == sorted(ids), set(ids) ^ set(taught + starters)
for b in books:
    for fid in b[6]:
        t = next(t for t, fl in tiers.items() if any(f['id'] == fid for f in fl))
        assert t == b[1], (b[0], fid)

lines = []
for (bid, hsk, aff, en, hz, price, fish, desc) in books:
    aff = min(aff, 3)
    extra = (f'minHsk = {hsk}, ' if hsk else '') + (f'minAffinity = {aff}, ' if aff else '')
    lines.append(f'            new ItemDef {{ id = "{bid}", {extra}english = "{en}", hanzi = "{hz}", category = ItemCategory.Book, price = {price}, unique = true,')
    lines.append(f'                teachesFish = new[] {{ {", ".join(chr(34) + x + chr(34) for x in fish)} }}, model = "FurnitureKit/books", modelScale = 0.2f,')
    lines.append(f'                description = "{desc}" }},')
book_code = '\n'.join(lines) + '\n'

p = os.path.join(ROOT, 'Assets', 'Scripts', 'Economy', 'Catalog.cs')
s = open(p, encoding='utf-8').read().replace('\r\n', '\n')
start = s.index('            new ItemDef { id = "book_basics"')
end = s.index('\n', s.index('id = "book_legends"'))
end = s.index('\n', s.index('description', end)) + 1 if 'description' in s[end:end + 400].split('new ItemDef')[0] else end + 1
# remove the whole old book block (from book_basics to the line ending the book_legends entry)
blk_end = s.index('\n', s.index('description = ', s.rindex('category = ItemCategory.Book'))) + 1  # through the last book (re-runnable)
s = s[:start] + book_code + s[blk_end:]
s = s.replace('items = new[] { "book_basics", "book_shore", "book_rocks", "book_open", "book_legends" } }',
              'items = new[] { ' + ', '.join(f'"{b[0]}"' for b in books) + ' } }')
open(p, 'w', encoding='utf-8', newline='\r\n').write(s)
print('fish', len(ids), 'books', len(books))
