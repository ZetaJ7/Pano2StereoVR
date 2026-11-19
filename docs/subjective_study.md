# 主观实验模式（第四版，2026-11-19 第二轮走查后）

设计依据：论文仓库 `Revisions/Revision20261113_E5_SubjectiveStudy.md` 第 0 节；数据契约：`StudyTools/README.md`（v4）；试次清单：`StudyTools/make_trial_plans.py`（plan 格式 4）；11-19 两轮桌面走查问题与修改：`docs/walkthrough_issues_20261119.md`。

## 1. 文件

| 位置 | 内容 |
| --- | --- |
| `Assets/Scenes/StudyScene.unity` | 只含一个 `StudyController` 物体（已挂好三个着色器）；相机、球面、UI、播放器都在运行时由代码搭建。可用菜单 `Tools/Pano2StereoVR/Create Study Scene` 重新生成 |
| `Assets/Scripts/Study/` | `StudyController`（会话、语言、签到、结束页与重新开始）、`StudyController.Steps`（SSQ、戴头显、立体视、注视练习、第二部分、第一部分分组与重评、事后问卷）、`StudySessionState`（一次会话的全部状态，重新开始时整体清空）、`StudyPlan`（清单解析）、`StudyRig`（头部位姿、3DoF、重定向）、`StudyClipPlayer`（两路 VideoPlayer 预加载、自定节奏循环播放）、`StudyUi`（电脑屏幕与头显两套 UI）、`StudyTheme`（统一配色、字号、字重与圆角）、`StudyMarkup`（文字表中 `**…**` 强调标记的解析）、`StudyShapes`（圆角矩形、卡片阴影、复选框对勾的形状生成，运行时做成九宫格精灵，工程不需要图片资源）、`StudyButtonStyle`（按钮文字随状态变色，选中的答案加粗）、`StudyPointer`（手柄射线与扳机计数）、`StudyDataLogger`（CSV/JSON）、`RandomDotStereogram`、`StudySettings`（路径与会话模式）、`StudyTexts`（单语文字表）、`MiniJson`、`StudyAutopilot` |
| `Assets/Scripts/Study/Resources/StudyTexts.json` | **全部界面文字的中、英两套文本（草稿，待定稿）**。每条有 `zh`、`en` 两列，界面只显示所选语言；`*_note` 为条目说明（SSQ 易混淆条目），以小字显示。文字中用两个星号括起需要强调的词（如 `只**转动眼睛**`），界面显示为加粗并变色（电脑屏幕墨蓝，头显内琥珀色；按钮上的文字只加粗）；三道评分题以 `**关键词**：问题` 开头，关键词显示为大标题、问题显示在其下。答案选项（SSQ 程度、五级量表、−3…+3、形状）不加强调标记，测试会检查。可直接改这个文件；打包后也可在 exe 旁放 `study_texts.json` 覆盖其中任意条目，无需重新打包 |
| `Assets/Shaders/StudyStereoTB.shader` | 上下排列立体 ERP（上半为左眼），按视线方向计算 ERP 坐标；ERP 中心列 = 内容正前方 |
| `Assets/Shaders/StudyStereoQuadTB.shader`、`StudyUnlitColor.shader` | 立体视测试图、朝向十字、注视练习圆点与射线 |
| `Assets/Tests/EditMode/Study/` | EditMode 测试（清单解析与第一部分分组、flow、随机点视差与像素换算、CSV 字段顺序与 README 一致、两种语言的文字表完整、会话状态重置、字段初始化中不创建引擎对象、强调标记成对且两种语言一致、答案选项不带强调、圆角与阴影形状） |
| `tools/make_test_clips.py` | 生成桌面检查用的小尺寸测试片段（1024×1024、3 s、左右眼不同标记）到 `Stimuli/` |

`Stimuli/`、`StudyData/`、`StudyData_backup/` 已加入 `.gitignore`，不提交。

## 2. 运行

### 2.1 准备
1. 生成试次清单：`python StudyTools/make_trial_plans.py --config StudyTools/study_config.example.json --participants P01-P30 --out StudyTools/plans`
2. 刺激放到 `Stimuli/`，文件名 `<clip_id>__<condition>.mp4`，上下排列（上半左眼）。桌面检查可先用 `python tools/make_test_clips.py --plans StudyTools/plans --out Stimuli --ffmpeg D:/WorkSpace/ffmpeg/bin/ffmpeg.exe` 生成测试片段。
3. 会话模式：默认 `formal`，签到时清单中任何刺激缺失都不能开始（签到页显示一行错误，缺失文件清单写入 Unity 日志与 `events.csv`）。走查或自动驾驶用 `qa`：缺失片段以占位图案播放，`session.json` 标记 `mode: qa`；状态栏另有“跳到下一组”（PgDn）与“跳到下一部分”（Shift+PgDn），跳过的试次标记 `skipped = 1`，分析不使用。第一部分的 DeepSynthesis、MotionParallax 刺激尚未生成，目前只能在 QA 模式下走查（或用 `tools/make_test_clips.py` 按新清单补生成测试片段）。
4. 路径与模式默认值（编辑器内）：清单 `StudyTools/plans`，刺激 `Stimuli`，数据 `StudyData`，备份 `StudyData_backup`，模式 `formal`。可在工程根目录（打包后为 exe 所在目录）放 `study_settings.json` 修改：
   ```json
   {"plans_dir": "plans", "stimuli_dir": "E:/P2S_Stimuli", "data_dir": "StudyData", "backup_dir": "F:/P2S_Backup", "participant_id": "", "mode": "formal"}
   ```
   也可用命令行：`--plans-dir`、`--stimuli-dir`、`--data-dir`、`--backup-dir`、`--texts`、`--participant P05`、`--mode qa`、`--qa`。

### 2.2 桌面模式（无头显，检查流程）
菜单 `Tools/Pano2StereoVR/Runtime Target/Desktop Screen` → 打开 `StudyScene` → Play。头显内的面板会显示在 Game 窗口中，可用鼠标点选；按住右键拖动或方向键模拟转头；`E` 切换显示左眼或右眼画面（核对左右眼）。随机点立体图单眼看不出形状，属设计预期。走查需要占位片段时，在 `study_settings.json` 中设 `"mode": "qa"`。

### 2.3 VR 模式（Pico 4 Ultra，PICO Connect 有线串流，SteamVR 为 OpenXR 运行时）
菜单 `Tools/Pano2StereoVR/Runtime Target/VR OpenXR` → Play（或打包后运行）。电脑屏幕显示语言选择、签到、SSQ、休息计时、事后问卷、结束页和主试状态栏；头显内显示指导语和评分面板，被试用手柄射线指向按钮、扣扳机确认（两只手柄都可以，最后扣扳机的那只为当前指针）。自定节奏的片段不需要瞄准按钮，扣任一扳机即可进入下一段。`Assets/XR/Settings/OpenXR Package Settings.asset` 中已启用 HTC Vive、Valve Index、Khronos Simple 与 Oculus Touch 交互配置。

## 3. 主试操作清单

1. 语言选择页选“中文”或“English”（之后全部界面只用所选语言）。
2. 签到页输入编号 → “加载清单”。页面只显示一行加载结果；该编号已有数据时提示将存入带时间戳的新文件夹；正式模式缺刺激时显示一行错误且不能开始。清单摘要写入 `events.csv` 与 `session.json`。
3. 勾选“已口头提醒”“纸质知情同意书与基本信息表已签署”→ 开始。
4. SSQ 前测（`S_pre`）：被试在电脑上点选 16 项（易混淆条目下方有小字说明）。
5. 协助戴头显：电脑屏幕显示醒目标题和三步说明；调好后按空格或“继续”（以当前朝向为正前方）。
6. 立体视测试（1 个练习 + 4、2、1 像素各 2 次；可选“看不清”，计为不正确）。
7. 第一部分（四种方法：Mono、Ours、DeepSynthesis、MotionParallax）：先练习（逐段评分），再按组进行。每组开始显示“第 k / N 组：新的场景”；组内同一场景的四种条件依次播放，每段后评立体感、观看舒适度、画面瑕疵（五级，从 5 到 1 竖排，日常用语描述）；组末显示本组评分汇总（“第 1 段、第 2 段……”，不显示条件名），每段可“回放”（扣扳机或空格立即返回，评分保留）或“重评”，确认后“继续”。
8. 第一部分结束：头显内与电脑屏幕同时醒目提示“请摘下头显休息，并在电脑屏幕上填写问卷”，被试填 SSQ `S_mid1`。
9. 休息：电脑屏幕显示休息计时（至少 `min_break_s` 秒）；计时满后出现“准备好了，开始”，由被试自选时机点击；实际休息时长写入 `session.json` 的 `break_s`。
10. SSQ `S_mid2`（第二部分前的基线），然后协助重新戴头显（重定正前方）。手动 Recenter（状态栏按钮或 R 键）在注视练习的对准与圆点阶段、第二部分从对准到后一段播完（含转头提示）期间不可用，只能在说明页、问题页和试次之间使用；被屏蔽时状态栏提示 3 s，`events.csv` 记 `recenter_blocked`。
11. 注视练习：黄十字与半径 15° 的一圈彩色圆点。被试把头转向十字，头部在十字 5° 内保持 1 s 后自动开始（十字变绿，外圈进度环点亮）；圆点依次亮起，被试只转眼不转头；头部偏离一旦超过阈值即中断本次练习并提示重做，最多 3 次（阈值与次数待用户确认），结果写入 `gazetraining.csv`。
12. 第二部分（PA 对 PAw）：每试次先显示黄十字（参考方向），头部在 5° 内保持 1 s 后自动开始播放（不需扳机）；前一段 → 灰屏 → 后一段（画面下方小字标出“前一段”“后一段”）；每段播完一遍后循环，被试扣扳机（桌面为空格）进入下一段；播放中头部偏离十字一旦超过注视练习的阈值（用户 11-19 头显实测后由 5° 改为 7.5°），该段立即中断，提示后重新对准并从头重播（后一段中断只重播后一段）；同一段重播 3 次后再转头时，被试选择“不再提醒”（该段从头再播一次，第二部分余下的播放不再因转头中断，数据标记）或“继续努力调整”（继续重播，再满 3 次时再次询问）；之后问题 1（有/没有）→ 若“有”则问题 2（−3 前一段好得多 到 +3 后一段好得多）。记录对准耗时、开始时偏离、每段观看时长、循环次数、最大头部偏离和重播次数。
13. 第二部分结束：提示摘下头显，填 SSQ 后测（`S_post`），再填事后问卷（只有一道开放问题：想反馈的内容或不舒服的地方，选填）→ 结束页。电脑屏幕显示“实验完成，谢谢参与”和“请在登记本记录学号并签字，离开前带齐随身物品”，头显内提示摘下头显。写盘与备份结果只写入日志；备份失败时状态栏出现一条红字提示。
14. 结束页两个按钮在写盘与备份完成后才可点：“结束”（再确认一次后退出程序；编辑器内停止播放）；“重新开始”（清空上一位被试的全部状态，重新读取设置与文字表，回到语言选择页；下一位被试写入新文件夹）。

状态栏显示：编号、模式、当前步骤、试次号与条件、累计丢帧、XR 状态、预览眼、数据目录；QA 模式下另有两个跳过按钮。`R` 重新定正前方（记入 `events.csv`），`Esc` 或“中止”中止（须填原因；已完成部分保存，`session.json` 标记 `aborted`）。

## 4. 快捷键（桌面检查用；被试在头显中用手柄）

| 场合 | 键 |
| --- | --- |
| 指导语、继续、自定节奏片段进入下一段、回放返回 | 空格 / 回车（片段与回放只认空格） |
| 立体视测试 | 1–4（按形状按钮顺序），5 或 0 为“看不清” |
| 第二部分问题 1 | Y / N |
| 第二部分问题 2 | 1–7 对应 −3 … +3 |
| 第一部分评分 | 1–5（即分值） |
| 第一部分组末汇总 | 1–9 重评第 k 段，空格继续（回放用鼠标点“回放”） |
| 朝向对准（桌面模拟） | 右键拖动或方向键把视线移回十字 |
| 主试 | R 重定向，Esc 中止，E 切换预览眼（仅桌面模式）；QA 模式 PgDn 跳到下一组，Shift+PgDn 跳到下一部分 |

## 5. 输出（`StudyData/<编号>/`）

`session.json`、`checkin.json`、`stereotest.csv`、`gazetraining.csv`、`part1.csv`、`part1_revisions.csv`、`part1_replays.csv`、`part2.csv`、`ssq.csv`、`headpose.csv`（播放期间逐显示帧；第二部分 yaw/pitch 相对片段参考方向，第一部分相对内容正前方）、`playback.csv`、`postquestionnaire.json`、`events.csv`，字段与 `StudyTools/README.md` v4 一致（第一部分评分项的界面名称与字段对应见 README）。练习与第二部分每个试次结束即写盘；第一部分组内的评分每次作答即写入 `part1_revisions.csv`，`part1.csv` 在本组确认“继续”时写入最终评分。

## 6. 已知限制与待核实

- **立体视测试为自制粗筛**：视差以渲染像素计（练习 8 px，正式 4、2、1 px），运行时由 XR 左眼投影与眼图纹理宽度换算视野中心一个渲染像素的角宽；无 XR 时用配置的每眼 2160 px、水平视场 105°（待按 Pico 4 Ultra 官方规格核实），约 249″/px。渲染像素经镜片与串流后与面板像素不一一对应，PICO Connect 的二次压缩可能抹掉 1 px 视差；需上机确认各档可辨。只能筛出明显的立体视异常，未与 Randot 做等效验证；通过标准由用户定，程序与分析默认只报告。
- **注视练习只验证“没有转头”**：头显无眼动追踪，不能验证眼睛确实看了偏轴目标。偏离阈值 5°、最多 3 次为待确认的默认值；最后一次仍超过阈值时实验继续，数据中标记。
- **自定节奏观看**偏离 P.919 的固定时长呈现；前后两段观看时长可能不同，已逐段记录，论文需说明。第一部分是否同样自定节奏（默认否）待用户确认。
- **朝向对准**：范围 5° 为用户 11-19 暂定值，需在头显实测后调整；保持 1 s 后自动开始。
- **第一部分四种方法**：DeepSynthesis 与 MotionParallax 的原生分辨率低于 4K，需放大；用户 11-19 决定两者统一用同一种可控的确定性插值放大到与 Ours、Mono 刺激相同的分辨率（建议 FFmpeg `v360` 等距柱状到等距柱状、上下排列、lanczos 插值，参数固定并写入刺激清单），不用学习型超分；论文方法中需写明这两种方法为原生输出插值放大；MotionParallax 的深度编码只覆盖 0.27–3 m；刺激尚未生成。组内四段与组末回放使第一部分时长明显增加（每人 36 个正式试次），需预实验核对时长。
- **评分项改名**：三项评分改用日常用语（立体感、观看舒适度、画面瑕疵）及其五级描述，与 BT.500 量表的对应关系需在方法中重新说明。
- **第一部分分组与组末重评、回放**：同一场景的条件连续呈现并允许组内回放与改分后，第一部分由单刺激绝对评分（ACR）变为组内多刺激比较评分，方法描述与标准依据需核实合适的标准条款后再写。
- **第二轮修改尚未在编辑器中运行**：C# 用 Unity 自带的 Roslyn 与引用库离线编译通过，EditMode 测试用 Unity 自带的 Mono 运行通过；朝向对准的十字变色与进度环、加大后的字号与面板尺寸、回放、QA 跳过、休息页需在编辑器中确认。
- **11-19 视觉优化尚未在编辑器中运行**（走查记录第 44 条）：离线编译与 EditMode 测试通过，版面只按尺寸核算过，没有看过 Unity 实际渲染。需在编辑器中确认：标题与主按钮的加粗效果（系统字体粗体）、圆角与卡片阴影、带“请摘下头显”横幅的 SSQ 页中文版是否不用滚动、头显内评分页（关键词大标题 + 五个带分数圆标的选项，面板高 1060）与本组评分页（每段一行底色）是否排得下、琥珀色强调在头显中是否过亮。设计示意图（HTML 绘制，非 Unity 实拍）在 `Builds/ui_preview/`。
- **4K 解码**：正式刺激为 5768×5768（上下排列）HEVC。Windows 上 VideoPlayer 走 Media Foundation，需安装 HEVC 视频扩展；测试机能否不丢帧需实测（看状态栏丢帧数和 `playback.csv`）。测试片段为 H.264 1024×1024。
- 头部只跟踪旋转（球面随头部位置移动）；渲染的球面半径 500 m，眼间距引入的附加视差约 26″，低于头显像素角。
- Screen Space Overlay 画布在 XR 模式下应只显示在电脑镜像窗口，需在头显中确认没有串入。
- 界面字体为系统字体 Microsoft YaHei（缺失时依次用 Microsoft YaHei UI、SimHei、Arial），工程未打包字体文件；测试机需装有中文字体。
- 文字表、SSQ 中文译法与说明、事后问卷题目均为草稿。
