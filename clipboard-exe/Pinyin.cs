namespace ClipboardExe;

/// <summary>
/// 拼音首字母搜索——映射表从 Web 版 app.js PY_GROUPS 原样提取（23 组 3755 常用字）。
/// 对齐 Web strToPy/pyInitial：取每个汉字拼音首字母，搜索词命中拼音首字母序列即匹配。
/// </summary>
public static class Pinyin
{
    private static readonly System.Collections.Generic.Dictionary<char, char> Map = BuildMap();
    private static System.Collections.Generic.Dictionary<char, char> BuildMap()
    {
        var map = new System.Collections.Generic.Dictionary<char, char>();
            foreach (var c in "啊阿埃挨哎唉哀皑癌蔼矮艾碍爱隘鞍氨安俺按暗岸胺案肮昂盎凹敖熬翱袄傲奥懊澳") map[c] = 'A';
            foreach (var c in "芭捌扒叭吧笆八疤巴拔跋靶把耙坝霸罢爸白柏百摆佰败拜稗斑班搬扳般颁板版扮拌伴瓣半办") map[c] = 'B';
            foreach (var c in "绊邦帮梆榜膀绑棒磅蚌镑傍谤苞胞包褒剥薄雹保堡饱宝抱报暴豹鲍爆杯碑悲卑北辈背贝钡倍") map[c] = 'B';
            foreach (var c in "狈备惫焙被奔苯本笨崩绷甭泵蹦迸逼鼻比鄙笔彼碧蓖蔽毕毙毖币庇痹闭敝弊必壁臂避陛鞭边") map[c] = 'B';
            foreach (var c in "编贬扁便变卞辨辩辫遍标彪膘表鳖憋别瘪彬斌濒滨宾摈兵冰柄丙秉饼炳病并玻菠播拨钵波博") map[c] = 'B';
            foreach (var c in "勃搏铂箔伯帛舶脖膊渤驳捕卜哺补埠不布步簿部怖") map[c] = 'B';
            foreach (var c in "擦猜裁材才财睬踩采彩菜蔡餐参蚕残惭惨灿苍舱仓沧藏操糙槽曹草厕策侧册测层蹭插叉茬茶") map[c] = 'C';
            foreach (var c in "查碴搽察岔差诧拆柴豺搀掺蝉馋谗缠铲产阐颤昌猖场尝常偿肠厂敞畅唱倡超抄钞朝嘲潮巢吵") map[c] = 'C';
            foreach (var c in "炒车扯撤掣彻澈郴臣辰尘晨忱沉陈趁衬撑称城橙成呈乘程惩澄诚承逞骋秤吃痴持池迟弛驰耻") map[c] = 'C';
            foreach (var c in "齿侈尺赤翅斥炽充冲虫崇宠抽酬畴踌稠愁筹仇绸瞅丑臭初出橱厨躇锄雏滁除楚础储矗搐触处") map[c] = 'C';
            foreach (var c in "揣川穿椽传船喘串疮窗幢床闯创吹炊捶锤垂春椿醇唇淳纯蠢戳绰疵茨磁雌辞慈瓷词此刺赐次") map[c] = 'C';
            foreach (var c in "聪葱囱匆从丛凑粗醋簇促蹿篡窜摧崔催脆瘁粹淬翠村存寸磋撮搓措挫错伺畜曾椎") map[c] = 'C';
            foreach (var c in "搭达答瘩打大呆歹傣戴带殆代贷袋待逮怠耽担丹单郸掸胆旦氮但惮淡诞弹蛋当挡党荡档刀捣") map[c] = 'D';
            foreach (var c in "蹈倒岛祷导到稻悼道盗德得的蹬灯登等瞪凳邓堤低滴迪敌笛狄涤翟嫡抵底地蒂第帝弟递缔颠") map[c] = 'D';
            foreach (var c in "掂滇碘点典靛垫电佃甸店惦奠淀殿碉叼雕凋刁掉吊钓调跌爹碟蝶迭谍叠丁盯叮钉顶鼎锭定订") map[c] = 'D';
            foreach (var c in "丢东冬董懂动栋侗恫冻洞兜抖斗陡豆逗痘都督毒犊独读堵睹赌杜镀肚度渡妒端短锻段断缎堆") map[c] = 'D';
            foreach (var c in "兑队对墩吨蹲敦顿囤钝盾遁掇哆多夺垛躲朵跺舵剁惰堕") map[c] = 'D';
            foreach (var c in "蛾峨鹅俄额讹娥恶厄扼遏鄂饿恩而儿耳尔饵洱二贰") map[c] = 'E';
            foreach (var c in "发罚筏伐乏阀法珐藩帆番翻樊矾钒繁凡烦反返范贩犯饭泛坊芳方肪房防妨仿访纺放菲非啡飞") map[c] = 'F';
            foreach (var c in "肥匪诽吠肺废沸费芬酚吩氛分纷坟焚汾粉奋份忿愤粪丰封枫蜂峰锋风疯烽逢冯缝讽奉凤佛否") map[c] = 'F';
            foreach (var c in "夫敷肤孵扶拂辐幅氟符伏俘服浮涪福袱弗甫抚辅俯釜斧腑府腐赴副覆赋复傅付阜父腹负富讣") map[c] = 'F';
            foreach (var c in "附妇缚咐") map[c] = 'F';
            foreach (var c in "噶嘎该改概钙盖溉干甘杆柑竿肝赶感秆敢赣冈刚钢缸肛纲岗港杠篙皋高膏羔糕搞镐稿告哥歌") map[c] = 'G';
            foreach (var c in "搁戈鸽胳疙割革葛格阁隔铬个各给根跟耕更庚羹埂耿梗工攻功恭龚供躬公宫弓巩汞拱贡共钩") map[c] = 'G';
            foreach (var c in "勾沟苟狗垢构购够辜菇咕箍估沽孤姑鼓古蛊骨谷股故顾固雇刮瓜剐寡挂褂乖拐怪棺关官冠观") map[c] = 'G';
            foreach (var c in "管馆罐惯灌贯光广逛瑰规圭硅归龟闺轨鬼诡癸桂柜跪贵刽辊滚棍锅郭国果裹过咯傀炔") map[c] = 'G';
            foreach (var c in "蛤哈骸孩海氦亥害骇酣憨邯韩含涵寒函喊罕翰撼捍旱憾悍焊汗汉夯杭航壕嚎豪毫郝好耗号浩") map[c] = 'H';
            foreach (var c in "呵喝荷菏核禾和何合盒貉阂河涸赫褐鹤贺嘿黑痕很狠恨哼亨横衡恒轰哄烘虹鸿洪宏弘红喉侯") map[c] = 'H';
            foreach (var c in "猴吼厚候后呼乎忽瑚壶葫胡蝴狐糊湖弧虎唬护互沪户花哗华猾滑画划化话槐徊怀淮坏欢环桓") map[c] = 'H';
            foreach (var c in "还缓换患唤痪豢焕涣宦幻荒慌黄磺蝗簧皇凰惶煌晃幌恍谎灰挥辉徽恢蛔回毁悔慧卉惠晦贿秽") map[c] = 'H';
            foreach (var c in "会烩汇讳诲绘荤昏婚魂浑混豁活伙火获或惑霍货祸") map[c] = 'H';
            foreach (var c in "击圾基机畸稽积箕肌饥迹激讥鸡姬绩缉吉极棘辑籍集及急疾汲即嫉级挤几脊己蓟技冀季伎祭") map[c] = 'J';
            foreach (var c in "剂悸济寄寂计记既忌际妓继纪嘉枷夹佳家加荚颊贾甲钾假稼价架驾嫁歼监坚尖笺间煎兼肩艰") map[c] = 'J';
            foreach (var c in "奸缄茧检柬碱硷拣捡简俭剪减荐鉴践贱见键箭件健舰剑饯渐溅涧建僵姜将浆江疆蒋桨奖讲匠") map[c] = 'J';
            foreach (var c in "酱降蕉椒礁焦胶交郊浇骄娇嚼搅铰矫侥脚狡角饺缴绞剿教酵轿较叫窖揭接皆秸街阶截劫节桔") map[c] = 'J';
            foreach (var c in "杰捷睫竭洁结解姐戒藉芥界借介疥诫届巾筋斤金今津襟紧锦仅谨进靳晋禁近烬浸尽劲荆兢茎") map[c] = 'J';
            foreach (var c in "睛晶鲸京惊精粳经井警景颈静境敬镜径痉靖竟竞净炯窘揪究纠玖韭久灸九酒厩救旧臼舅咎就") map[c] = 'J';
            foreach (var c in "疚鞠拘狙疽居驹菊局咀矩举沮聚拒据巨具距踞锯俱句惧炬剧捐鹃娟倦眷卷绢撅攫抉掘倔爵觉") map[c] = 'J';
            foreach (var c in "决诀绝均菌钧军君峻俊竣浚郡骏茄") map[c] = 'J';
            foreach (var c in "槛喀咖卡开揩楷凯慨刊堪勘坎砍看康慷糠扛抗亢炕考拷烤靠坷苛柯棵磕颗科壳咳可渴克刻客") map[c] = 'K';
            foreach (var c in "课肯啃垦恳坑吭空恐孔控抠口扣寇枯哭窟苦酷库裤夸垮挎跨胯块筷侩快宽款匡筐狂框矿眶旷") map[c] = 'K';
            foreach (var c in "况亏盔岿窥葵奎魁馈愧溃坤昆捆困括扩廓阔") map[c] = 'K';
            foreach (var c in "垃拉喇蜡腊辣啦莱来赖蓝婪栏拦篮阑兰澜谰揽览懒缆烂滥琅榔狼廊郎朗浪捞劳牢老佬姥酪烙") map[c] = 'L';
            foreach (var c in "涝勒乐雷镭蕾磊累儡垒擂肋类泪棱楞冷厘梨犁黎篱狸离漓理李里鲤礼莉荔吏栗丽厉励砾历利") map[c] = 'L';
            foreach (var c in "傈例俐痢立粒沥隶力璃哩俩联莲连镰廉怜涟帘敛脸链恋炼练粮凉梁粱良两辆量晾亮谅撩聊僚") map[c] = 'L';
            foreach (var c in "疗燎寥辽潦了撂镣廖料列裂烈劣猎琳林磷霖临邻鳞淋凛赁吝拎玲菱零龄铃伶羚凌灵陵岭领另") map[c] = 'L';
            foreach (var c in "令溜琉榴硫馏留刘瘤流柳六龙聋咙笼窿隆垄拢陇楼娄搂篓漏陋芦卢颅庐炉掳卤虏鲁麓碌露路") map[c] = 'L';
            foreach (var c in "赂鹿潞禄录陆戮驴吕铝侣旅履屡缕虑氯律率滤绿峦挛孪滦卵乱掠略抡轮伦仑沦纶论萝螺罗逻") map[c] = 'L';
            foreach (var c in "锣箩骡裸落洛骆络") map[c] = 'L';
            foreach (var c in "妈麻玛码蚂马骂嘛吗埋买麦卖迈脉瞒馒蛮满蔓曼慢漫谩芒茫盲氓忙莽猫茅锚毛矛铆卯茂冒帽") map[c] = 'M';
            foreach (var c in "貌贸么玫枚梅酶霉煤没眉媒镁每美昧寐妹媚门闷们萌蒙檬盟锰猛梦孟眯醚靡糜迷谜弥米秘觅") map[c] = 'M';
            foreach (var c in "泌蜜密幂棉眠绵冕免勉娩缅面苗描瞄藐秒渺庙妙蔑灭民抿皿敏悯闽明螟鸣铭名命谬摸摹蘑模") map[c] = 'M';
            foreach (var c in "膜磨摩魔抹末莫墨默沫漠寞陌谋牟某拇牡亩姆母墓暮幕募慕木目睦牧穆") map[c] = 'M';
            foreach (var c in "拿哪呐钠那娜纳氖乃奶耐奈南男难囊挠脑恼闹淖呢馁内嫩能妮霓倪泥尼拟你匿腻逆溺蔫拈年") map[c] = 'N';
            foreach (var c in "碾撵捻念娘酿鸟尿捏聂孽啮镊镍涅您柠狞凝宁拧泞牛扭钮纽脓浓农弄奴努怒女暖虐疟挪懦糯") map[c] = 'N';
            foreach (var c in "诺辗") map[c] = 'N';
            foreach (var c in "哦欧鸥殴藕呕偶沤") map[c] = 'O';
            foreach (var c in "辟泊脯啪趴爬帕怕琶拍排牌徘湃派攀潘盘磐盼畔判叛乓庞旁耪胖抛咆刨炮袍跑泡呸胚培裴赔") map[c] = 'P';
            foreach (var c in "陪配佩沛喷盆砰抨烹澎彭蓬棚硼篷膨朋鹏捧碰坯砒霹批披劈琵毗啤脾疲皮匹痞僻屁譬篇偏片") map[c] = 'P';
            foreach (var c in "骗飘漂瓢票撇瞥拼频贫品聘乒坪苹萍平凭瓶评屏坡泼颇婆破魄迫粕剖扑铺仆莆葡菩蒲埔朴圃") map[c] = 'P';
            foreach (var c in "普浦谱曝瀑") map[c] = 'P';
            foreach (var c in "期欺栖戚妻七凄漆柒沏其棋奇歧畦崎脐齐旗祈祁骑起岂乞企启契砌器气迄弃汽泣讫掐恰洽牵") map[c] = 'Q';
            foreach (var c in "扦钎铅千迁签仟谦乾黔钱钳前潜遣浅谴堑嵌欠歉枪呛腔羌墙蔷强抢橇锹敲悄桥瞧乔侨巧鞘撬") map[c] = 'Q';
            foreach (var c in "翘峭俏窍切且怯窃钦侵亲秦琴勤芹擒禽寝沁青轻氢倾卿清擎晴氰情顷请庆琼穷秋丘邱球求囚") map[c] = 'Q';
            foreach (var c in "酋泅趋区蛆曲躯屈驱渠取娶龋趣去圈颧权醛泉全痊拳犬券劝缺瘸却鹊榷确雀裙群") map[c] = 'Q';
            foreach (var c in "然燃冉染瓤壤攘嚷让饶扰绕惹热壬仁人忍韧任认刃妊纫扔仍日戎茸蓉荣融熔溶容绒冗揉柔肉") map[c] = 'R';
            foreach (var c in "茹蠕儒孺如辱乳汝入褥软阮蕊瑞锐闰润若弱") map[c] = 'R';
            foreach (var c in "匙撒洒萨腮鳃塞赛三叁伞散桑嗓丧搔骚扫嫂瑟色涩森僧莎砂杀刹沙纱傻啥煞筛晒珊苫杉山删") map[c] = 'S';
            foreach (var c in "煽衫闪陕擅赡膳善汕扇缮墒伤商赏晌上尚裳梢捎稍烧芍勺韶少哨邵绍奢赊蛇舌舍赦摄射慑涉") map[c] = 'S';
            foreach (var c in "社设砷申呻伸身深娠绅神沈审婶甚肾慎渗声生甥牲升绳省盛剩胜圣师失狮施湿诗尸虱十石拾") map[c] = 'S';
            foreach (var c in "时什食蚀实识史矢使屎驶始式示士世柿事拭誓逝势是嗜噬适仕侍释饰氏市恃室视试收手首守") map[c] = 'S';
            foreach (var c in "寿授售受瘦兽蔬枢梳殊抒输叔舒淑疏书赎孰熟薯暑曙署蜀黍鼠属术述树束戍竖墅庶数漱恕刷") map[c] = 'S';
            foreach (var c in "耍摔衰甩帅栓拴霜双爽谁水睡税吮瞬顺舜说硕朔烁斯撕嘶思私司丝死肆寺嗣四似饲巳松耸怂") map[c] = 'S';
            foreach (var c in "颂送宋讼诵搜艘擞嗽苏酥俗素速粟僳塑溯宿诉肃酸蒜算虽隋随绥髓碎岁穗遂隧祟孙损笋蓑梭") map[c] = 'S';
            foreach (var c in "唆缩琐索锁所厦") map[c] = 'S';
            foreach (var c in "塌他它她塔獭挞蹋踏胎苔抬台泰酞太态汰坍摊贪瘫滩坛檀痰潭谭谈坦毯袒碳探叹炭汤塘搪堂") map[c] = 'T';
            foreach (var c in "棠膛唐糖倘躺淌趟烫掏涛滔绦萄桃逃淘陶讨套特藤腾疼誊梯剔踢锑提题蹄啼体替嚏惕涕剃屉") map[c] = 'T';
            foreach (var c in "天添填田甜恬舔腆挑条迢眺跳贴铁帖厅听烃汀廷停亭庭挺艇通桐酮瞳同铜彤童桶捅筒统痛偷") map[c] = 'T';
            foreach (var c in "投头透凸秃突图徒途涂屠土吐兔湍团推颓腿蜕褪退吞屯臀拖托脱鸵陀驮驼椭妥拓唾") map[c] = 'T';
            foreach (var c in "挖哇蛙洼娃瓦袜歪外豌弯湾玩顽丸烷完碗挽晚皖惋宛婉万腕汪王亡枉网往旺望忘妄威巍微危") map[c] = 'W';
            foreach (var c in "韦违桅围唯惟为潍维苇萎委伟伪尾纬未蔚味畏胃喂魏位渭谓尉慰卫瘟温蚊文闻纹吻稳紊问嗡") map[c] = 'W';
            foreach (var c in "翁瓮挝蜗涡窝我斡卧握沃巫呜钨乌污诬屋无芜梧吾吴毋武五捂午舞伍侮坞戊雾晤物勿务悟误") map[c] = 'W';
            foreach (var c in "昔熙析西硒矽晰嘻吸锡牺稀息希悉膝夕惜熄烯溪汐犀檄袭席习媳喜铣洗系隙戏细瞎虾匣霞辖") map[c] = 'X';
            foreach (var c in "暇峡侠狭下夏吓掀锨先仙鲜纤咸贤衔舷闲涎弦嫌显险现献县腺馅羡宪陷限线相厢镶香箱襄湘") map[c] = 'X';
            foreach (var c in "乡翔祥详想响享项巷橡像向象萧硝霄削哮嚣销消宵淆晓小孝校肖啸笑效楔些歇蝎鞋协挟携邪") map[c] = 'X';
            foreach (var c in "斜胁谐写械卸蟹懈泄泻谢屑薪芯锌欣辛新忻心信衅星腥猩惺兴刑型形邢行醒幸杏性姓兄凶胸") map[c] = 'X';
            foreach (var c in "匈汹雄熊休修羞朽嗅锈秀袖绣墟戌需虚嘘须徐许蓄酗叙旭序恤絮婿绪续轩喧宣悬旋玄选癣眩") map[c] = 'X';
            foreach (var c in "绚靴薛学穴雪血勋熏循旬询寻驯巡殉汛训讯逊迅吁") map[c] = 'X';
            foreach (var c in "压押鸦鸭呀丫芽牙蚜崖衙涯雅哑亚讶焉咽阉烟淹盐严研蜒岩延言颜阎炎沿奄掩眼衍演艳堰燕") map[c] = 'Y';
            foreach (var c in "厌砚雁唁彦焰宴谚验殃央鸯秧杨扬佯疡羊洋阳氧仰痒养样漾邀腰妖瑶摇尧遥窑谣姚咬舀药要") map[c] = 'Y';
            foreach (var c in "耀椰噎耶爷野冶也页掖业叶曳腋夜液一壹医揖铱依伊衣颐夷遗移仪胰疑沂宜姨彝椅蚁倚已乙") map[c] = 'Y';
            foreach (var c in "矣以艺抑易邑屹亿役臆逸肄疫亦裔意毅忆义益溢诣议谊译异翼翌绎茵荫因殷音阴姻吟银淫寅") map[c] = 'Y';
            foreach (var c in "饮尹引隐印英樱婴鹰应缨莹萤营荧蝇迎赢盈影颖硬映哟拥佣臃痈庸雍踊蛹咏泳涌永恿勇用幽") map[c] = 'Y';
            foreach (var c in "优悠忧尤由邮铀犹油游酉有友右佑釉诱又幼迂淤于盂榆虞愚舆余俞逾鱼愉渝渔隅予娱雨与屿") map[c] = 'Y';
            foreach (var c in "禹宇语羽玉域芋郁遇喻峪御愈欲狱育誉浴寓裕预豫驭鸳渊冤元垣袁原援辕园员圆猿源缘远苑") map[c] = 'Y';
            foreach (var c in "愿怨院曰约越跃钥岳粤月悦阅耘云郧匀陨允运蕴酝晕韵孕轧") map[c] = 'Y';
            foreach (var c in "长匝砸杂栽哉灾宰载再在咱攒暂赞赃脏葬遭糟凿藻枣早澡蚤躁噪造皂灶燥责择则泽贼怎增憎") map[c] = 'Z';
            foreach (var c in "赠扎喳渣札铡闸眨栅榨咋乍炸诈摘斋宅窄债寨瞻毡詹粘沾盏斩崭展蘸栈占战站湛绽樟章彰漳") map[c] = 'Z';
            foreach (var c in "张掌涨杖丈帐账仗胀瘴障招昭找沼赵照罩兆肇召遮折哲蛰辙者锗蔗这浙珍斟真甄砧臻贞针侦") map[c] = 'Z';
            foreach (var c in "枕疹诊震振镇阵蒸挣睁征狰争怔整拯正政帧症郑证芝枝支吱蜘知肢脂汁之织职直植殖执值侄") map[c] = 'Z';
            foreach (var c in "址指止趾只旨纸志挚掷至致置帜峙制智秩稚质炙痔滞治窒中盅忠钟衷终种肿重仲众舟周州洲") map[c] = 'Z';
            foreach (var c in "诌粥轴肘帚咒皱宙昼骤珠株蛛朱猪诸诛逐竹烛煮拄瞩嘱主著柱助蛀贮铸筑住注祝驻抓爪拽专") map[c] = 'Z';
            foreach (var c in "砖转撰赚篆桩庄装妆撞壮状锥追赘坠缀谆准捉拙卓桌琢茁酌啄着灼浊兹咨资姿滋淄孜紫仔籽") map[c] = 'Z';
            foreach (var c in "滓子自渍字鬃棕踪宗综总纵邹走奏揍租足卒族祖诅阻组钻纂嘴醉最罪尊遵昨左佐柞做作坐座") map[c] = 'Z';
        return map;
    }

    /// <summary>取文本的拼音首字母序列（非汉字原样保留）。</summary>
    public static string Initials(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF && Map.TryGetValue(ch, out var py)) sb.Append(py);
            else if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>query 是否命中 text 的拼音首字母序列（子串匹配，忽略大小写）。</summary>
    public static bool Match(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        var initials = Initials(text);
        var q = query.Trim();
        if (initials.Length == 0 || q.Length == 0) return false;
        return initials.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}
