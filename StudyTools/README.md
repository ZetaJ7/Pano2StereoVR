# StudyTools：主观实验的试次清单与分析程序

设计文档：`D:\SUSTechWork\Articles\Pano2Stereo_Paper\Revisions\Revision20261113_E5_SubjectiveStudy.md` 第 0 节。

依赖：Python 3.9+、numpy、scipy、matplotlib（不需要 pandas）。本机可用 `D:\Anaconda3\envs\pano2stereo\python.exe`。

## 流程

```powershell
# 1. 按被试编号生成试次清单（可复现：同一 study_id、config_version、被试编号得到同一顺序）
#    正式 P01–P40（用户 11-20：至少 35 人；40 为 4 的倍数，循环拉丁方完全平衡），预实验 T01–T05 放在单独的文件夹，不占正式编号
python make_trial_plans.py --config study_config.example.json --participants P01-P40 --out plans --check-files
python make_trial_plans.py --config study_config.example.json --participants T01-T05 --out plans_pilot
#    预实验会话：study_settings.json 设 "plans_dir": "StudyTools/plans_pilot"（或命令行 --plans-dir），数据建议另设 data_dir

# 2. Unity 实验模式读取 plans/<被试编号>.json，数据写到 StudyData/<被试编号>/

# 3. 全部采集完后出报告
python analyze_study.py --data StudyData --config study_config.json --out report
#    判定规则（筛除口径、头部偏离阈值、丢帧阈值、立体视通过标准、单尾或双尾、主要比较组、只分析哪些会话模式）
#    放在 analysis_config.json，用 --analysis analysis_config.json 传入；未设定的规则在报告中只给数或按所有口径分别给数，不替用户选。

# 程序自检（模拟数据，只用于检查程序，不是实验结果）
python simulate_study_data.py --plans plans --out _simulated_data
python analyze_study.py --data _simulated_data --config study_config.json --out _simulated_report
```

## 现场流程（plan 格式 5，2026-11-20）

由配置的 `flow` 决定，默认：语言选择（中文 / English，写入 `session.json`）→ 签到（主试输入编号、确认口头提醒、确认纸质知情同意书与基本信息表已签署）→ SSQ 前测 `S_pre`（电脑）→ 戴头显 → 头显内立体视测试（兼作适应）→ 第一部分 → 摘头显并填 SSQ `S_mid1` → 休息（`break`，至少 `min_break_s` 秒，之后被试自选开始）→ SSQ `S_mid2` → 重新戴头显（重定正前方）→ 自然注视范围测量 `eyerange` → 注视练习 `gazetraining` → 第二部分 → 摘头显并填 SSQ 后测 `S_post` → 简短事后问卷 → 结束页（“结束”或“重新开始”下一位被试）。11-15 曾按“第二部分更累，先做”排在前面，用户 11-19 改回先 1 后 2。

自然注视范围测量（用户 11-20）放在注视练习之前，此时被试还没有被要求“头部不动”，指导语也不提头部。用户 11-20 改为连续向外扫动（方案 A，自然观看是连续的搜索）：每次先面向中央十字（沿用 `alignment.tolerance_deg`，保持 `eye_range.align_dwell_s` 秒，不显示偏离读数），十字消失后一个彩色圆点从十字处出发，沿一个方向匀速向外移动到最大偏心度后消失；被试像平时一样自然地用目光跟随圆点，不需要按键。每个显示帧记录圆点偏心度、头部相对参考方向的 yaw、pitch、头部相对本次扫动开始时朝向的转动量及其朝扫动方向的分量；每次扫动另记一行汇总（头部转动首次超过阈值时圆点的偏心度）；判据一满足，该次扫动立即结束、圆点消失（用户 11-20，`stop_at_onset`），未满足的扫动照常移到最大偏心度。16 次扫动后显示结果页（头显内，电脑屏幕同步显示给主试）：8 个方向围绕中心点的示意图，每个方向一条径向条表示该方向的眼动范围（两次的中位数，删失显示为“≥40°”），10°、20°、30°、40° 参考圆与标注，下方一行总体值 a；被试按“下一步”（扳机或空格）后结束本步骤，QA 跳过照常有效。测量期间不能手动 Recenter。程序据此按暂定规则得到被试的自然眼动范围 a，并为第二部分选定 PAw 修正半径 θ（见配置要点）。

QA 模式下电脑端状态栏有“跳到下一组”（PgDn）与“跳到下一部分”（Shift+PgDn）：第一部分跳过当前组（练习阶段跳过剩余练习），第二部分跳过当前试次，其余步骤直接结束该步骤（自然注视范围测量跳过余下全部扫动，θ 用默认 15°）。跳过的试次在 `stereotest.csv`、`eyerange.csv`、`part1.csv`、`part2.csv` 中 `skipped = 1`，并写入 `events.csv`，分析程序不使用。正式模式下不显示，快捷键无效。

主试跳过（F8，正式模式与 QA 模式都可用，用户 11-20）：第一部分某段播放或首次评分时、第二部分一个试次从对准到答题结束时，按 F8 在电脑上弹出确认框“跳过当前视频？ Y 确认 / N 或 Esc 取消”。确认框打开时其他快捷键（包括第二部分问题的 Y/N、Esc 的中止面板、R、Space）、被试手柄与鼠标点击全部暂停，头显内显示“请稍候”遮挡面板，第二部分的对准计时与转头检测暂停；关闭后下一帧恢复。确认框打开期间正在播放的片段暂停（视频停在当前帧，观看时长、循环次数、自定节奏的最短时长都不计；`headpose.csv` 在暂停期间没有行），取消后从暂停的帧继续，被试不会漏看；`events.csv` 记 `clip_pause_start`、`clip_pause_end`（步骤、试次、暂停时的观看时间、继续或跳过）。确认后：第一部分只跳过这一段（不评分，`skipped = 1`、`skip_reason = staff`，本组汇总中显示“已跳过”，不能回放、重评，本组其他片段照常）；第二部分跳过整个试次（两段与问题，`skipped = 1`、`skip_reason = staff`）。其他步骤（含立体视、组末汇总、回放、重评）F8 不起作用。`events.csv` 记 `staff_skip_dialog`、`staff_skip_confirmed` / `staff_skip_cancelled`（打开时长）与 `staff_skip`（试次）。分析程序排除 `skipped = 1` 的行（QA 与主试跳过都排除），报告中分列主试跳过的行数。

设定正前方（用户 11-20 18:50，取代此前的 Recenter 规则）：程序不再自动重设正前方。正前方只由主试按键设定：在两个戴头显步骤（第一部分前、第二部分前重新戴头显）中，请被试坐正、自然平视前方，按 R（水平与俯仰一起设为正前方）或 Shift+R（只设水平，俯仰保持水平，给对画面倾斜敏感的被试）；该步骤的“继续”（与空格）在按下 R 或 Shift+R 之前不可用，电脑屏幕显示“正前方已设定”与施加的水平、俯仰偏移。设定后一直有效，直到下一次手动设定。理由（用户）：自然头部姿势不一定水平，强制俯仰为 0 可能不舒服；俯仰设定会使内容地平线倾斜被试当时的俯仰角。其他时段按 R / Shift+R（或状态栏按钮）为紧急重设：只在试次之间以及说明页、问题页、汇总页上可用，电脑上弹出确认框“重设正前方（含俯仰 / 仅水平）？ Y 确认 / N 或 Esc 取消”，与 F8 确认框一样暂停其他输入、头显内显示“请稍候”；Y 时被试当时的头部朝向成为正前方。测量头部朝向（自然注视范围、注视练习、第二部分对准到后一段播完）或片段播放时按键无效，记 `recenter_ignored`。因此被试在某一部分中途改变姿势时，只能在这些允许的时段用紧急重设纠正，否则要到下一个戴头显步骤。`events.csv` 记 `recenter`（戴头显步骤）或 `recenter_emergency`（方式、施加的水平与俯仰偏移、步骤与试次），以及 `recenter_emergency_dialog`、`recenter_emergency_cancelled`、`recenter_ignored`；`session.json` 的 `recenters` 为全部记录。所有头部角度由相机的世界旋转换算，已包含完整的装置旋转。头显面板不再放在面板出现时的头部朝向上：说明、提示、问题、休息、结果、组末汇总等面板固定在设定的正前方（内容水平 0、俯仰 0），立体视测试图同样在正前方；第二部分试次内的面板（转头提示、继续或重播、问题 1 与 2、“不再提醒”选择、两段之间的“后一段”）固定在该试次的参考方向；对准十字的提示在参考方向。例外：第一部分可前进与回放提示条仍随头部（持续跟随，不是出现时放一次）；“请稍候”遮挡面板随视线（为了遮挡）；本次会话第一次设定正前方之前，戴头显步骤的面板随头部水平朝向，以便调头带时能看到。

第二部分全景旋转（用户 11-20）：每个第二部分试次（练习 train_near 与正式 near_*，前后两段，含转头后的重播）播放片段时，全景球绕竖直轴旋转 −`reference_yaw_deg`，使该试次的注视区域（`reference_yaw_deg` 为 ERP 水平角）显示在设定的正前方，被试不需要转头；只有水平旋转，上下排列的立体不受影响。灰屏、说明页、第一部分、自然注视范围与注视练习中球不旋转。对准十字、试次内面板、快进图标、提示与转头检测都按显示方向（正前方，水平 0、俯仰为该试次的 `reference_pitch_deg`）。刺激与清单不变（`reference_yaw_deg` 仍是 ERP 中注视区域的水平角，PAw 窗口按 ERP 渲染，随内容一起旋转）。旋转后 ERP 左右接缝（ERP 水平角 ±180°）显示在水平角 180° − `reference_yaw_deg`：near_A 120°，near_B −60°（视野左缘附近），near_C −120°，near_D 150°，near_E 与 near_F −150°，train_near −90°。

知情同意、基本信息、视力与色觉自报改为到场纸质填写（11-19 用户决定）；基本信息表按 `pre_session.csv` 的列录入，见下。`pre_session_form.md`（线上表单草稿）已停用。

会话模式：`formal`（默认，刺激缺失时不能开始）或 `qa`（允许占位片段，`session.json` 标记 `mode: qa`）。在 `study_settings.json` 中设 `"mode": "qa"`，或命令行 `--qa` / `--mode qa`；自动驾驶 `-studyAutopilot` 一律为 qa。

## 配置要点（`study_config.example.json`）

- `stereotest`：视差以显示像素计（`practice_disparity_px` 8，`disparities_px` [4, 2, 1]，`repeats_per_level` 2，点大小 `dot_px` 3）。程序运行时由 XR 左眼投影矩阵与眼图纹理宽度算出视野中心一个渲染像素的角宽（无 XR 时用 `display_fallback` 的每眼宽度与水平视场），测试图纹理按“1 texel ≈ 1 像素”取尺寸，并把像素与角秒写入 `stereotest.csv`。被试可选“看不清”（`cant_tell`），计为不正确。程序不含通过标准。
- `gaze_training`：黄十字在参考方向，`n_dots` 个不同颜色的点在半径 `radius_deg` 的圆上依次亮起，每点 `dwell_s` 秒；记录每点期间头部相对参考方向的最大偏离；头部偏离一旦超过 `max_head_deviation_deg`，立即中断本次练习并提示重做，最多 `max_attempts` 次，最后一次仍超过时继续实验并在数据中标记。
- `eye_range`（用户 11-20，连续扫动 `mode = continuous`；第一版的离散圆点方式已删除）：`directions_deg`（8 个方向，0° = 上、顺时针，与注视练习圆点相同）× `repeats`（2）= 16 次扫动，顺序按被试随机（由被试种子派生的独立随机流，其余部分的顺序不变）；圆点以 `speed_deg_s`（8°/s）从 0° 匀速移到 `max_eccentricity_deg`（40°），每次 5 s；`dot_size_deg` 1.5、`align_dwell_s` 0.5 s、`gap_s` 0.3 s。每次扫动的 `head_onset_ecc_deg` = 头部相对扫动开始时朝向的大圆角转动（`measure = head_rot_deg`）首次超过 `head_threshold_deg` 时圆点的偏心度；`stop_at_onset`（true，用户 11-20）时扫动在这一帧结束；到最大偏心度仍未超过记为删失。a 的规则 `rule = median_onset_ecc`（暂定）：全部扫动 `head_onset_ecc_deg` 的中位数，删失按 `max_eccentricity_deg` 计。`head_threshold_deg` 3.0° 为暂定值，预实验后再定；逐帧原始数据保留在 `eyerange_trace.csv`，规则可事后更改。PAw 修正半径 θ 取 `paw_theta_variants_deg`（10、15、20、25、30°；衰减带 5° 不变）中最接近 a 的一个（超出范围取端点，距离相同取较小者）；步骤关闭、不在流程中或被跳过时用 `default_paw_theta_deg`（15°，论文默认值），并记入 `events.csv` 与 `session.json`。速度、最大偏心度、次数、圆点大小、对准保持与间隔为预实验用值，预实验后按实测调整。
- `stimulus_folders`（用户 11-20）：各部分按试次角色（`training`、`dummy`、`main`）给出子文件夹，`main` 可用 `{cell}`；`file_pattern` 与 `part2.file_patterns` 中的 `{folder}` 替换为该子文件夹。只影响文件名，不影响任何随机顺序。
- `part2.file_patterns`：按条件覆盖 `file_pattern`；`paw` 为 `{clip_id}__paw_t{paw_theta}.mp4`。清单中 `{paw_theta}` 保留为占位符，程序在自然注视范围测量后替换为被试的 θ（如 `near_A__paw_t20.mp4`）；PA 文件名不变。签到时的缺失刺激检查与 `make_trial_plans.py --check-files` 覆盖全部 θ 变体。
- `alignment`：第二部分每个试次与注视练习开始前，被试把头转向黄十字；头部在参考方向 `tolerance_deg` 内连续保持 `dwell_s` 秒后自动开始（不需按扳机），中途离开则重新计时。进入范围时十字变绿，外圈进度环随保持时间点亮，提示中显示当前偏离。`tolerance_deg` 5° 为用户 11-19 暂定，实测后再调整；`dwell_s` 1.0 s。
- `part1.viewing`、`part2.viewing`：`self_paced`（播完一遍后循环，按扳机或空格进入下一段）、`min_view_s`（`null` = 完整播放一遍）、`max_view_s`（0 = 不限）；`part1.viewing.allow_review_on_rerate`（组末汇总每段可“回放”，用户 11-19 决定开启）。
- 第二部分播放时不显示文字（用户 11-19）：“前一段”写在开始前的对准提示里，“后一段”在两段之间的灰屏显示；数据中仍记为 A（前一段）、B（后一段）。某段完整播放一遍、可以前进时，在该试次参考方向（对准十字所在处）显示一个半透明的小快进图标（两个向右的三角形，约 1.875° 宽，用户 11-20），前进或该段结束时消失，`events.csv` 记 `part2_advance_cue`。
- 第一部分条件（用户 11-19 决定）：`mono`、`ours`、`deepsynthesis`、`motionparallax`，9 个片段 × 4 = 36 个正式试次，另 2 个练习、1 组 dummy（1 个片段 × 4 条件）。后两种方法的刺激尚未生成，QA 模式以占位图案播放。
- 第一部分按片段分组：每组为同一片段在全部条件下连续呈现；练习试次在组之前逐个评分；dummy 片段按片段分成不计分的组，排在正式组之前（被试看到的组号连续计数，不区分 dummy）。组内条件顺序按循环拉丁方平衡（每位被试每个条件在每个位置出现的次数相差不超过 1），组顺序随机，相邻两组不是同一片段。每组结束显示本组评分汇总（只写“第 1 段、第 2 段……”，不显示条件名），每段可“回放”（同一片段、同一播放方式，按扳机或空格立即返回，评分保留）或“重评”，再继续。
- 待用户确认的默认值列在配置的 `pending_user_decision` 中。

## 刺激文件

刺激放在 `stimuli_dir`（默认 `Stimuli/`）下按类型分的子文件夹（用户 11-19/11-20，`docs/stimulus_plan_20261119.md` 第 9 节），清单中的文件名带子文件夹（正斜杠），由配置的 `stimulus_folders` 按试次角色生成：第一部分练习 `part1/practice`、dummy `part1/dummy`、正式按片段的 `cell`（`part1/virtual_indoor`、`part1/real_indoor`、`part1/real_outdoor`）；第二部分练习 `part2/practice`、正式 `part2/near`。程序在 `stimuli_dir` 下解析（Windows 上换成反斜杠），缺失检查、`stimuli_manifest_sha256` 与 `playback.csv` 的 `file` 都用带子文件夹的相对名。

```text
Stimuli/
  part1/practice/        train_indoor__ours.mp4、train_outdoor__mono.mp4
  part1/dummy/           dummy_indoor__<condition>.mp4
  part1/virtual_indoor/  vin_*__<condition>.mp4
  part1/real_indoor/     rin_*__<condition>.mp4
  part1/real_outdoor/    rout_*__<condition>.mp4
  part2/practice/        train_near__pa.mp4、train_near__paw_t<θ>.mp4
  part2/near/            near_*__pa.mp4、near_*__paw_t<θ>.mp4
```

文件名 `<clip_id>__<condition>.mp4`。上下排列立体 ERP（上半为左眼），HEVC。条件名：第一部分 `mono`、`ours`、`deepsynthesis`、`motionparallax`；第二部分 `pa`、`paw`。第二部分 PAw 按修正半径分 5 个变体：`<clip_id>__paw_t<θ>.mp4`（θ = 10、15、20、25、30，如 `near_A__paw_t20.mp4`），第二部分全部片段（含练习 `train_near`）都需要 5 个变体与 1 个 PA。 QA 用的测试片段：`python tools/make_test_clips.py --plans StudyTools/plans --out Stimuli`（按同一子文件夹写入；原先放在 `Stimuli/` 根目录的测试片段与样片不会被找到，需要移到对应子文件夹或移到 `Stimuli/_samples/`）。

## 第一部分评分项与数据字段（11-19 第二轮改名）

| 界面名称（中文 / English） | 数据字段 | 5 → 1 的描述 |
| --- | --- | --- |
| 立体感 / Sense of depth | `depth_quality` | 非常强、比较强、一般、比较弱、几乎没有 |
| 观看舒适度 / Viewing comfort | `visual_comfort` | 非常舒服、比较舒服、有点不舒服、比较不舒服、非常不舒服 |
| 画面瑕疵 / Visual artifacts | `artifact_annoyance` | 没有察觉到、有一点（不影响观看）、比较明显（有些影响）、很明显（影响观看）、非常严重 |

字段名沿用，分值方向不变（5 最好）。描述已从 BT.500 量表的措辞改为日常用语，与 BT.500 量表的对应关系需在方法中重新说明。

## Unity 必须写出的数据（数据契约 v6，2026-11-20；每个会话一个文件夹 `StudyData/<被试编号>/`）

v6 相对 v5（v6 尚未用于采集，11-20 发布前修订过五次，版本号仍为 v6。第五次（用户 11-20）：第二部分全景按试次旋转，`headpose.csv` 末尾增加 `content_yaw_offset_deg`。第四次（用户 11-20）：`part1.csv`、`part2.csv` 在 `skipped` 后增加 `skip_reason`（`staff` = 主试 F8 跳过，`qa` = QA 跳过，空 = 未跳过）；`session.json` 增加 `recenters`；`events.csv` 增加 `staff_skip*`，`recenter` 写明方式与偏移。第一次：离散圆点的 `eyerange.csv` 增加 `onset_dev_deg`、`head_rot_deg`、`head_rot_along_deg`，a 改用 `head_rot_deg`。第二次（用户 11-20，方案 A）：改为连续向外扫动，`eyerange.csv` 改为每次扫动一行（字段见下表），新增逐帧的 `eyerange_trace.csv`，`session.json` 的 `eye_range` 改为扫动的参数、各方向中位数与删失数。第三次（用户 11-20，Pico 实测后）：扫动在头部起动时提前结束，`eyerange.csv` 在 `censored` 后增加 `stopped_early`；新增结果页，`session.json` 的 `eye_range` 增加 `stop_at_onset`、`stopped_early`、`a_shown`、`per_direction[].shown`、`result_shown_utc`、`result_confirm_utc`、`result_time_on_page_s`；`events.csv` 增加 `eyerange_result_shown`、`eyerange_result_confirmed`、`part2_advance_cue`，`eyerange_result` 写入结果页显示的数值与选定的 θ）：新增 `eyerange.csv`（自然注视范围测量，每次扫动一行）与 `eyerange_trace.csv`（每个显示帧一行）；`part2.csv` 在 `skipped` 前增加 `paw_theta_deg`；`session.json` 增加 `eye_range`；`events.csv` 增加 `eyerange_sweep`（每次扫动的对准耗时、开始时偏离与结果）、`eyerange_result`（a、θ、各方向中位数与结果页显示的数值）、`eyerange_result_shown`、`eyerange_result_confirmed`、`paw_theta`、`part2_advance_cue`（第二部分某段可前进、快进图标出现）；`playback.csv` 的 `file` 为替换 θ 后的实际文件名。

v5 相对 v4：`part2.csv` 在 `start_dev_deg` 后增加 `restarts_a`、`restarts_b`、`reminder_off_a`、`reminder_off_b`；`session.json` 可含 `part2_reminder_off`；`postquestionnaire.json` 只含开放问题 `comments`；`headpose.csv`、`playback.csv` 的 `segment` 增加 `A_stopped`、`B_stopped`（播放中头部转动而中断的观看）。

v4 相对 v3：`stereotest.csv`、`part1.csv`、`part2.csv` 增加 `skipped`；`part1.csv` 增加 `replays`；新增 `part1_replays.csv`；`part2.csv` 增加 `align_s`、`start_dev_deg`；SSQ 测量时点为 `S_pre`、`S_mid1`、`S_mid2`、`S_post`；`session.json` 增加 `break_s`。

所有 CSV 为 UTF-8，第一行为表头。时间单位为秒，角度单位为度。同一编号已有数据时，新会话写入 `<被试编号>__<UTC 时间>` 文件夹，不覆盖。

| 文件 | 字段 |
| --- | --- |
| `session.json` | `eye_range`（`mode`：`continuous`；`a_deg`：按暂定规则得到的 a，未测量时为 null；`rule`：`median_onset_ecc`；`measure`：`head_rot_deg`；`speed_deg_s`；`max_eccentricity_deg`；`stop_at_onset`；`head_threshold_deg`；`rule_provisional`；`per_direction`：每个方向的 `direction_deg`、`median_head_onset_ecc_deg`（删失按最大偏心度计）、`shown`（结果页显示的文字，如 `23°`、`≥40°`）、`n`、`censored`；`a_shown`（结果页显示的 a）；`censored`：删失的扫动数；`stopped_early`：在头部起动时结束的扫动数；`result_shown_utc`、`result_confirm_utc`（QA 跳过时为 null）、`result_time_on_page_s`；`chosen_paw_theta_deg`；`paw_theta_source`：`measured` / `default_disabled` / `default_skipped` / `default_not_in_flow` / `default_no_block` / `aborted`；`skipped`：未按测量结果选定 θ 时为 true；`enabled`、`default_paw_theta_deg`、`paw_theta_variants_deg`、`n_sweeps`、`n_answered`）、`break_s`（实际休息时长）、`participant_id`、`status`（`complete` / `aborted` / `in_progress`）、`abort_reason`、`mode`（`formal` / `qa`）、`language`（`zh` / `en`）、`plan_seed`、`config_sha256`、`plan_file`、`plan_file_sha256`、`plan_summary`（flow、各部分试次数与组数、观看选项、注视练习参数、刺激数与缺失数、输出文件夹）、`app_version`、`stimuli_manifest_sha256`、`stimuli_missing`、`hmd`、`xr_runtime`、`display_refresh_hz`、`stereotest_arcsec_per_px`、`stereotest_px_source`、`stereotest_texture_px`、`stereotest_dot_px`、`gaze_training`（次数、最后一次是否在阈值内、每次最大偏离）、`start_utc`、`end_utc`、`dropped_frames_total`。模拟数据另有 `simulated: true` |
| `checkin.json` | `participant_id`、`verbal_reminder_given`（主试勾选）、`paper_forms_confirmed`（主试勾选：纸质知情同意书与基本信息表已签署；v2 文件夹中为 `pre_session_confirmed`，分析程序两者都认）、`language`、`mode`、`timestamp_utc` |
| `stereotest.csv` | `participant_id, trial_index, block, shape, disparity_px, disparity_arcsec, arcsec_per_px, px_source, response, correct, rt_s, skipped`；`skipped` 为 1 时（QA 跳过）作答各列为空；`block` 为 `practice` / `main`；`response` 为形状名或 `cant_tell`；`px_source` 为 `xr_eye_texture` / `config_fallback` |
| `gazetraining.csv` | `participant_id, attempt, position, dot_id, dot_angle_deg, dwell_s, max_dev_deg, attempt_max_dev_deg, threshold_deg, attempt_passed`；每次练习每个亮起的点一行（中断的练习只到中断时的点为止，该行 `dwell_s` 为实际亮起时长）；`dot_angle_deg` 从正上方顺时针；`max_dev_deg` 为该点亮起期间头部相对参考方向的最大大圆角偏离 |
| `eyerange.csv` | `participant_id, sweep, direction_deg, repeat, speed_deg_s, onset_dev_deg, head_onset_ecc_deg, censored, stopped_early, max_head_rot_deg, skipped`；`stopped_early` 为 1 表示该次扫动在头部起动的那一帧结束（此时 `max_head_rot_deg` 刚过阈值）；每次扫动一行，按呈现顺序，扫动结束即写入；`sweep` 为清单中的扫动序号，`repeat` 为该方向第几次（0 起）；`direction_deg` 从正上方顺时针；`onset_dev_deg` 为扫动开始时头部相对参考方向的偏离（对准后的残留）；`head_onset_ecc_deg` 为头部相对扫动开始时朝向的转动首次超过 `head_threshold_deg` 时圆点的偏心度，删失（`censored = 1`，到最大偏心度仍未超过）时为空；`max_head_rot_deg` 为本次扫动中的最大转动；`skipped` 为 1 时（QA 跳过，或扫动中途被跳过）测量各列为空 |
| `eyerange_trace.csv` | `participant_id, sweep, direction_deg, repeat, t_s, dot_ecc_deg, head_yaw_deg, head_pitch_deg, head_rot_deg, head_rot_along_deg`；扫动期间每个显示帧一行（中途被跳过的扫动保留已显示的帧）；`t_s` 从圆点出发起算；`dot_ecc_deg` 为该帧圆点的偏心度；`head_yaw_deg`、`head_pitch_deg` 为头部相对参考方向的偏航与俯仰（右、上为正）；`head_rot_deg` 为相对扫动开始时头部朝向的大圆角转动；`head_rot_along_deg` 为这段转动朝扫动方向的分量（以开始时朝向为中心做等距方位投影，朝向扫动方向为正） |
| `part1.csv` | `participant_id, block, trial_index, group_index, position_in_group, clip_id, condition, repetition, depth_quality, visual_comfort, artifact_annoyance, rt_depth_s, rt_comfort_s, rt_artifact_s, initial_depth, initial_comfort, initial_artifact, revisions, view_s, loops, replays, skipped, skip_reason`；`skip_reason` 为 `staff`（主试 F8 跳过这一段，不评分）、`qa`（QA 跳过）或空；评分项名称与字段的对应见上节；`replays` 为组末回放次数；`skipped` 为 1 时为 QA 跳过（本组未确认），未作答的列为空；`block` 为 `training` / `dummy` / `main`；评分 1–5，三项评分为组末确认后的最终评分，`initial_*` 为第一次评分，`revisions` 为重评次数；练习试次 `group_index` 为 0，全部练习评完后进入“练习评分”汇总页（与正式组相同，可回放、重评），确认“继续”时写入；组内试次在本组确认“继续”时写入 |
| `part1_revisions.csv` | `participant_id, group_index, trial_index, clip_id, condition, event, revision, reviewed, depth_quality, visual_comfort, artifact_annoyance, t_utc`；每次评分即写一行：`event` 为 `initial` / `revision`，`revision` 为第几次重评，`reviewed` 为重评前是否重看（1/0）。中途中止时，未确认组的评分只在这里 |
| `part1_replays.csv` | `participant_id, group_index, position_in_group, trial_index, clip_id, condition, replay, t_start_utc, view_s, loops, completed`；组末汇总每次回放一行；`replay` 为该段第几次回放；`completed` 为是否至少完整播放一遍（1/0） |
| `part2.csv` | `participant_id, block, trial_index, clip_id, cond_a, cond_b, pair_type, different, preference, rt_q1_s, rt_q2_s, view_a_s, view_b_s, loops_a, loops_b, max_dev_a_deg, max_dev_b_deg, align_s, start_dev_deg, restarts_a, restarts_b, reminder_off_a, reminder_off_b, paw_theta_deg, skipped, skip_reason`；`skip_reason` 为 `staff`（主试 F8 跳过整个试次）、`qa` 或空；`paw_theta_deg` 为本会话的 PAw 修正半径（每行相同，PA–PA 配对也写）；A = 前一段、B = 后一段（界面只说“前一段 / 后一段”）；`pair_type` 为 `real` / `same`；`different` 为 1/0；`preference` 为 −3（前一段好得多）到 +3（后一段好得多），答"没有区别"时留空；`align_s` 为朝向对准耗时（从十字出现到自动开始），`start_dev_deg` 为开始时头部相对参考方向的偏离；`skipped` 为 1 时为 QA 跳过；`view_*_s` 为该段实际观看时长，`loops_*` 为完整播放遍数，`max_dev_*_deg` 为该段头部相对参考方向的最大偏离；播放中头部偏离一旦超过注视练习的阈值 `max_head_deviation_deg`，该段立即中断，提示后重新对准并从头重播，`restarts_*` 为该段的重播次数，`view_*`、`loops_*`、`max_dev_*` 只描述最后完整的一次观看；同一段重播 3 次后再转头，被试选择“不再提醒”或“继续努力调整”：选前者则该段从头再播一次，第二部分余下的播放不再因转头中断，`reminder_off_*` 为 1 表示该段最后完整的一次观看是在关闭提醒后播放的（选择记入 `events.csv` 的 `part2_reminder_choice` 与 `session.json` 的 `part2_reminder_off`） |
| `ssq.csv` | `participant_id, measurement, GD, FA, HE, ES, DF, IS, SW, NA, CO, FH, BV, DO, DC, VE, SA, BU`；`measurement` 为 `S_pre` / `S_mid1`（第一部分后）/ `S_mid2`（休息后、第二部分前）/ `S_post`（v3 及以前为 `S_mid`）；每项 0–3；QA 跳过的 SSQ 不写行 |
| `headpose.csv` | `participant_id, part, trial_index, segment, t_s, video_frame, yaw_deg, pitch_deg, roll_deg, content_yaw_offset_deg`；头部水平角先换算成内容（ERP）水平角（相机世界水平角 + 当前全景旋转偏移），第二部分的行再相对该片段的参考方向（含义与此前相同）；`content_yaw_offset_deg` 为当时施加的全景旋转偏移（第二部分片段播放时为该试次的 `reference_yaw_deg`，其他为 0，用户 11-20）；每个显示帧一行；`segment` 第一部分为 `clip`（回放为 `replay`），第二部分为 `A` / `B`（因头部转动中断的观看为 `A_stopped` / `B_stopped`）；**第二部分的 yaw、pitch 相对该片段的参考方向** |
| `playback.csv` | `participant_id, part, trial_index, segment, file, frames_expected, frames_shown, frames_dropped, dt_mean_ms, dt_p99_ms`；`file` 为相对 `stimuli_dir` 的文件名（带子文件夹，正斜杠，第二部分为替换 θ 后的名字）；自定节奏（循环）播放时 `frames_expected` 为观看时长 × 片段帧率，`frames_shown` 为显示过的（遍数, 帧号）组合数 |
| `postquestionnaire.json` | 事后问卷只有一道开放问题（反馈与不舒服的地方，选填）：`participant_id`、`comments`、`timestamp_utc` |
| `events.csv` | `t_utc, event, detail`；语言、清单加载（含清单文件 SHA-256）、缺失刺激清单、清单摘要、步骤开始结束、重定向、休息起止与时长、注视练习对准与每次结果、重评、回放、QA 跳过（`qa_skip*`）、播放异常、写盘与备份结果、结束页操作（退出、重新开始） |
| `session.json` 另含 | `flow`、`part_order`、`plan_format_version`、`data_contract_version`、`settings_source`、`stimuli_dir`、`recenters`（每次 Recenter：`t_utc`、`kind`、`step`、`trial`、`yaw_offset_deg`、`pitch_offset_deg`） |

`pre_session.csv`（纸质基本信息表录入后放在 `--data` 目录下，或用 `--pre-session` 指定）：`participant_id, consent, age, gender, vr_experience, corrected_vision_normal, color_vision_normal, epilepsy_history`。表中不录姓名；纸质同意书原件按 IRB 要求另行保存。

## 分析内容

- 跳过的试次（`skipped = 1`）不进入任何分析。
- 会话：以会话文件夹为单位，只分析 `status = complete` 的会话。同一编号有多个文件夹时在报告中列出；同一编号有多个 complete 会话时需用户裁决。`mode = qa` 的会话在报告中列出，`analysis_config.session_modes` 设定后只分析所列模式。
- MOS 与 95% CI：BT.500-15 Annex 1 to Part 1 式 (1)–(4)，δ = 1.96·S/√N。
- 观察者筛除：峰度法（§A1-2.3.1，标准注明适用于少于约 20 名观察者）与相关法（§A1-2.3.3，单刺激法 MCT = 0.7）都计算，报告按"不筛除 / 峰度法 / 相关法"三种口径分别给数。
- 第一部分：用组末确认后的最终评分；四种条件各自的 MOS；主要比较为 Ours − Mono（`part1_primary_pair`），每位被试按片段配对（同一片段两个条件都有评分）求差值，再对片段平均，Wilcoxon 符号秩检验（三项量表），报告配对数、差值中位数、bootstrap 95% CI、秩二列相关；其余配对（`part1_extra_pairs`，默认 Ours 对 DeepSynthesis、Ours 对 MotionParallax，待用户确认）同法计算，只作探索性报告，不进 Holm 主比较组；按片段类别分层（探索性）；逐片段 MOS；重评率与回放次数。
- 第二部分：逐被试 d′（对数线性校正），对 0 做 Wilcoxon 检验；偏好统一为"pose-aware 更好为正"，两种口径（答"没有区别"计 0；只用答"有区别"的试次）；逐片段命中率、虚报率、合并 d′、偏好均值。
- SSQ：P.919 §7.2.1 的 N、O、D、TS；`S_pre` → `S_mid1`（第一部分）、`S_mid2` → `S_post`（第二部分）与 `S_mid2` − `S_pre`（休息后的残余）只报数（差值中位数、均值、N）；主要比较组中的 SSQ 对比（`ssq_primary_contrast`，默认 `S_pre` → `S_post`）照旧检验。实际休息时长（`break_s`）一并报告。
- 立体视测试：逐被试各视差档（像素）正确数、“看不清”次数、″/px 与来源；通过标准属判定规则（`stereo_pass_rule`，如 `{"max_disparity_px": 2, "min_correct_per_level": 2}`），由用户定，未设定时只报告。
- 注视练习：逐被试练习次数、每次最大头部偏离、程序阈值、最后一次是否在阈值内，只报告。
- 自然注视范围：逐被试 a、θ、来源、删失数与提前结束数（`session.json` 记录值，并按 `eyerange_trace.csv` 逐帧数据与同一阈值、同一规则复算核对）；各方向 `head_onset_ecc_deg` 中位数（逐被试，以及各方向在被试间的分布：N、最小、四分位、中位数、最大），只报数。第二部分按 θ 分组的描述性结果（人数、命中率、虚报率、d′ 与偏好均值），不做检验，不新增剔除规则。
- 观看时长：第二部分每段观看时长、循环次数、每段最大头部偏离，第一部分每段观看时长，只报告分布。
- 主要比较组 Holm 校正；头部偏离角与丢帧的分布（阈值未设定时只报告）。

## 分析配置 `analysis_config.json`（草稿，2026-11-21，待用户过目）

依据 `docs/E5_AnalysisPlan_20261121.md` 第 1–3 节。程序用 `json.loads` 读配置，JSON 不能写注释；配置只用 `analyze_study.py` 中 `DEFAULT_ANALYSIS` 已有的键，说明写在这里。当前为草稿：用户过目、预实验后填入 `stereo_pass_rule` 并再次提交（早于第一位正式被试）之后才算冻结。

```powershell
python analyze_study.py --data StudyData --config study_config.example.json --analysis analysis_config.json --out report
```

| 键 | 取值 | 状态 |
| --- | --- | --- |
| `part1_primary_pair` | `["mono", "ours"]`（P1–P3 为 Ours − Mono） | 用户已定（11-21 晚） |
| `alternative` | `"two-sided"` | 用户已定 |
| `primary_family` | `P1, P2, P3, P4a, P4b, P5`，Holm 校正 | 用户已定 |
| `screening_primary` | `"none"`（主分析不筛除；峰度法与相关法口径照常给数，作敏感性分析） | 用户已定 |
| `stereo_pass_rule` | `null` | **预实验后填**：原则为只排除最大视差档也不能通过的被试，档位与通过次数在预实验后写入 |
| `session_modes` | `["formal"]` | 草稿，Claude 的建议 |
| `exclude_self_reported_vision` | `true` | 草稿，Claude 的建议 |
| `dropped_frame_fraction_threshold` | `0.05` | 草稿，Claude 的建议 |
| `head_deviation_threshold_deg` | `null`（不另设阈值，只报分布；第二部分线上已对头部偏离中断重播） | 草稿，Claude 的建议 |
| `preference_no_difference_as_zero` | `true`（P4b） | 草稿，Claude 的建议 |
| `ssq_primary_contrast` | `["S_pre", "S_post"]`（P5） | 草稿，Claude 的建议 |
| `correlation_mct` | `0.7` | 草稿，Claude 的建议 |
| `bootstrap_iterations`、`bootstrap_seed` | `10000`、`20261115` | 草稿，Claude 的建议 |
| `part1_extra_pairs` | Ours 对 DeepSynthesis、Ours 对 MotionParallax（探索性，不校正） | 草稿，Claude 的建议 |
| `ssq_descriptive_contrasts` | `S_pre`→`S_mid1`、`S_mid2`→`S_post`、`S_pre`→`S_mid2`（只报数） | 草稿，Claude 的建议（方案第 3 节） |
| `conditions_part1`、`conditions_part2` | 与程序默认相同 | 方案未单列；显式写出，便于冻结 |
