# StudyTools：主观实验的试次清单与分析程序

设计文档：`D:\SUSTechWork\Articles\Pano2Stereo_Paper\Revisions\Revision20261113_E5_SubjectiveStudy.md` 第 0 节。

依赖：Python 3.9+、numpy、scipy、matplotlib（不需要 pandas）。本机可用 `D:\Anaconda3\envs\pano2stereo\python.exe`。

## 流程

```powershell
# 1. 按被试编号生成试次清单（可复现：同一 study_id、config_version、被试编号得到同一顺序）
python make_trial_plans.py --config study_config.json --participants P01-P30 --out plans --check-files

# 2. Unity 实验模式读取 plans/<被试编号>.json，数据写到 StudyData/<被试编号>/

# 3. 全部采集完后出报告
python analyze_study.py --data StudyData --config study_config.json --out report
#    判定规则（筛除口径、头部偏离阈值、丢帧阈值、立体视通过标准、单尾或双尾、主要比较组、只分析哪些会话模式）
#    放在 analysis_config.json，用 --analysis analysis_config.json 传入；未设定的规则在报告中只给数或按所有口径分别给数，不替用户选。

# 程序自检（模拟数据，只用于检查程序，不是实验结果）
python simulate_study_data.py --plans plans --out _simulated_data
python analyze_study.py --data _simulated_data --config study_config.json --out _simulated_report
```

## 现场流程（plan 格式 4，2026-11-19 第二轮走查后）

由配置的 `flow` 决定，默认：语言选择（中文 / English，写入 `session.json`）→ 签到（主试输入编号、确认口头提醒、确认纸质知情同意书与基本信息表已签署）→ SSQ 前测 `S_pre`（电脑）→ 戴头显 → 头显内立体视测试（兼作适应）→ 第一部分 → 摘头显并填 SSQ `S_mid1` → 休息（`break`，至少 `min_break_s` 秒，之后被试自选开始）→ SSQ `S_mid2` → 重新戴头显（重定正前方）→ 注视练习 `gazetraining` → 第二部分 → 摘头显并填 SSQ 后测 `S_post` → 简短事后问卷 → 结束页（“结束”或“重新开始”下一位被试）。11-15 曾按“第二部分更累，先做”排在前面，用户 11-19 改回先 1 后 2。

QA 模式下电脑端状态栏有“跳到下一组”（PgDn）与“跳到下一部分”（Shift+PgDn）：第一部分跳过当前组（练习阶段跳过剩余练习），第二部分跳过当前试次，其余步骤直接结束该步骤。跳过的试次在 `stereotest.csv`、`part1.csv`、`part2.csv` 中 `skipped = 1`，并写入 `events.csv`，分析程序不使用。正式模式下不显示，快捷键无效。

知情同意、基本信息、视力与色觉自报改为到场纸质填写（11-19 用户决定）；基本信息表按 `pre_session.csv` 的列录入，见下。`pre_session_form.md`（线上表单草稿）已停用。

会话模式：`formal`（默认，刺激缺失时不能开始）或 `qa`（允许占位片段，`session.json` 标记 `mode: qa`）。在 `study_settings.json` 中设 `"mode": "qa"`，或命令行 `--qa` / `--mode qa`；自动驾驶 `-studyAutopilot` 一律为 qa。

## 配置要点（`study_config.example.json`）

- `stereotest`：视差以显示像素计（`practice_disparity_px` 8，`disparities_px` [4, 2, 1]，`repeats_per_level` 2，点大小 `dot_px` 3）。程序运行时由 XR 左眼投影矩阵与眼图纹理宽度算出视野中心一个渲染像素的角宽（无 XR 时用 `display_fallback` 的每眼宽度与水平视场），测试图纹理按“1 texel ≈ 1 像素”取尺寸，并把像素与角秒写入 `stereotest.csv`。被试可选“看不清”（`cant_tell`），计为不正确。程序不含通过标准。
- `gaze_training`：黄十字在参考方向，`n_dots` 个不同颜色的点在半径 `radius_deg` 的圆上依次亮起，每点 `dwell_s` 秒；记录每点期间头部相对参考方向的最大偏离；头部偏离一旦超过 `max_head_deviation_deg`，立即中断本次练习并提示重做，最多 `max_attempts` 次，最后一次仍超过时继续实验并在数据中标记。
- `alignment`：第二部分每个试次与注视练习开始前，被试把头转向黄十字；头部在参考方向 `tolerance_deg` 内连续保持 `dwell_s` 秒后自动开始（不需按扳机），中途离开则重新计时。进入范围时十字变绿，外圈进度环随保持时间点亮，提示中显示当前偏离。`tolerance_deg` 5° 为用户 11-19 暂定，实测后再调整；`dwell_s` 1.0 s。
- `part1.viewing`、`part2.viewing`：`self_paced`（播完一遍后循环，按扳机或空格进入下一段）、`min_view_s`（`null` = 完整播放一遍）、`max_view_s`（0 = 不限）；`part1.viewing.allow_review_on_rerate`（组末汇总每段可“回放”，用户 11-19 决定开启）。
- 第二部分播放时，画面下方小字标出“前一段”“后一段”（英文 first / second clip）；数据中仍记为 A（前一段）、B（后一段）。
- 第一部分条件（用户 11-19 决定）：`mono`、`ours`、`deepsynthesis`、`motionparallax`，9 个片段 × 4 = 36 个正式试次，另 2 个练习、1 组 dummy（1 个片段 × 4 条件）。后两种方法的刺激尚未生成，QA 模式以占位图案播放。
- 第一部分按片段分组：每组为同一片段在全部条件下连续呈现；练习试次在组之前逐个评分；dummy 片段按片段分成不计分的组，排在正式组之前（被试看到的组号连续计数，不区分 dummy）。组内条件顺序按循环拉丁方平衡（每位被试每个条件在每个位置出现的次数相差不超过 1），组顺序随机，相邻两组不是同一片段。每组结束显示本组评分汇总（只写“第 1 段、第 2 段……”，不显示条件名），每段可“回放”（同一片段、同一播放方式，按扳机或空格立即返回，评分保留）或“重评”，再继续。
- 待用户确认的默认值列在配置的 `pending_user_decision` 中。

## 刺激文件

`Stimuli/<clip_id>__<condition>.mp4`。上下排列立体 ERP（上半为左眼），HEVC。条件名：第一部分 `mono`、`ours`、`deepsynthesis`、`motionparallax`；第二部分 `pa`、`paw`。

## 第一部分评分项与数据字段（11-19 第二轮改名）

| 界面名称（中文 / English） | 数据字段 | 5 → 1 的描述 |
| --- | --- | --- |
| 立体感 / Sense of depth | `depth_quality` | 非常强、比较强、一般、比较弱、几乎没有 |
| 观看舒适度 / Viewing comfort | `visual_comfort` | 非常舒服、比较舒服、有点不舒服、比较不舒服、非常不舒服 |
| 画面瑕疵 / Visual artifacts | `artifact_annoyance` | 没有察觉到、有一点（不影响观看）、比较明显（有些影响）、很明显（影响观看）、非常严重 |

字段名沿用，分值方向不变（5 最好）。描述已从 BT.500 量表的措辞改为日常用语，与 BT.500 量表的对应关系需在方法中重新说明。

## Unity 必须写出的数据（数据契约 v5，2026-11-19 第三轮；每个会话一个文件夹 `StudyData/<被试编号>/`）

v5 相对 v4：`part2.csv` 在 `start_dev_deg` 后增加 `restarts_a`、`restarts_b`、`reminder_off_a`、`reminder_off_b`；`session.json` 可含 `part2_reminder_off`；`postquestionnaire.json` 只含开放问题 `comments`；`headpose.csv`、`playback.csv` 的 `segment` 增加 `A_stopped`、`B_stopped`（播放中头部转动而中断的观看）。

v4 相对 v3：`stereotest.csv`、`part1.csv`、`part2.csv` 增加 `skipped`；`part1.csv` 增加 `replays`；新增 `part1_replays.csv`；`part2.csv` 增加 `align_s`、`start_dev_deg`；SSQ 测量时点为 `S_pre`、`S_mid1`、`S_mid2`、`S_post`；`session.json` 增加 `break_s`。

所有 CSV 为 UTF-8，第一行为表头。时间单位为秒，角度单位为度。同一编号已有数据时，新会话写入 `<被试编号>__<UTC 时间>` 文件夹，不覆盖。

| 文件 | 字段 |
| --- | --- |
| `session.json` | `break_s`（实际休息时长）、`participant_id`、`status`（`complete` / `aborted` / `in_progress`）、`abort_reason`、`mode`（`formal` / `qa`）、`language`（`zh` / `en`）、`plan_seed`、`config_sha256`、`plan_file`、`plan_file_sha256`、`plan_summary`（flow、各部分试次数与组数、观看选项、注视练习参数、刺激数与缺失数、输出文件夹）、`app_version`、`stimuli_manifest_sha256`、`stimuli_missing`、`hmd`、`xr_runtime`、`display_refresh_hz`、`stereotest_arcsec_per_px`、`stereotest_px_source`、`stereotest_texture_px`、`stereotest_dot_px`、`gaze_training`（次数、最后一次是否在阈值内、每次最大偏离）、`start_utc`、`end_utc`、`dropped_frames_total`。模拟数据另有 `simulated: true` |
| `checkin.json` | `participant_id`、`verbal_reminder_given`（主试勾选）、`paper_forms_confirmed`（主试勾选：纸质知情同意书与基本信息表已签署；v2 文件夹中为 `pre_session_confirmed`，分析程序两者都认）、`language`、`mode`、`timestamp_utc` |
| `stereotest.csv` | `participant_id, trial_index, block, shape, disparity_px, disparity_arcsec, arcsec_per_px, px_source, response, correct, rt_s, skipped`；`skipped` 为 1 时（QA 跳过）作答各列为空；`block` 为 `practice` / `main`；`response` 为形状名或 `cant_tell`；`px_source` 为 `xr_eye_texture` / `config_fallback` |
| `gazetraining.csv` | `participant_id, attempt, position, dot_id, dot_angle_deg, dwell_s, max_dev_deg, attempt_max_dev_deg, threshold_deg, attempt_passed`；每次练习每个亮起的点一行（中断的练习只到中断时的点为止，该行 `dwell_s` 为实际亮起时长）；`dot_angle_deg` 从正上方顺时针；`max_dev_deg` 为该点亮起期间头部相对参考方向的最大大圆角偏离 |
| `part1.csv` | `participant_id, block, trial_index, group_index, position_in_group, clip_id, condition, repetition, depth_quality, visual_comfort, artifact_annoyance, rt_depth_s, rt_comfort_s, rt_artifact_s, initial_depth, initial_comfort, initial_artifact, revisions, view_s, loops, replays, skipped`；评分项名称与字段的对应见上节；`replays` 为组末回放次数；`skipped` 为 1 时为 QA 跳过（本组未确认），未作答的列为空；`block` 为 `training` / `dummy` / `main`；评分 1–5，三项评分为组末确认后的最终评分，`initial_*` 为第一次评分，`revisions` 为重评次数；练习试次 `group_index` 为 0，全部练习评完后进入“练习评分”汇总页（与正式组相同，可回放、重评），确认“继续”时写入；组内试次在本组确认“继续”时写入 |
| `part1_revisions.csv` | `participant_id, group_index, trial_index, clip_id, condition, event, revision, reviewed, depth_quality, visual_comfort, artifact_annoyance, t_utc`；每次评分即写一行：`event` 为 `initial` / `revision`，`revision` 为第几次重评，`reviewed` 为重评前是否重看（1/0）。中途中止时，未确认组的评分只在这里 |
| `part1_replays.csv` | `participant_id, group_index, position_in_group, trial_index, clip_id, condition, replay, t_start_utc, view_s, loops, completed`；组末汇总每次回放一行；`replay` 为该段第几次回放；`completed` 为是否至少完整播放一遍（1/0） |
| `part2.csv` | `participant_id, block, trial_index, clip_id, cond_a, cond_b, pair_type, different, preference, rt_q1_s, rt_q2_s, view_a_s, view_b_s, loops_a, loops_b, max_dev_a_deg, max_dev_b_deg, align_s, start_dev_deg, restarts_a, restarts_b, reminder_off_a, reminder_off_b, skipped`；A = 前一段、B = 后一段（界面只说“前一段 / 后一段”）；`pair_type` 为 `real` / `same`；`different` 为 1/0；`preference` 为 −3（前一段好得多）到 +3（后一段好得多），答"没有区别"时留空；`align_s` 为朝向对准耗时（从十字出现到自动开始），`start_dev_deg` 为开始时头部相对参考方向的偏离；`skipped` 为 1 时为 QA 跳过；`view_*_s` 为该段实际观看时长，`loops_*` 为完整播放遍数，`max_dev_*_deg` 为该段头部相对参考方向的最大偏离；播放中头部偏离一旦超过注视练习的阈值 `max_head_deviation_deg`，该段立即中断，提示后重新对准并从头重播，`restarts_*` 为该段的重播次数，`view_*`、`loops_*`、`max_dev_*` 只描述最后完整的一次观看；同一段重播 3 次后再转头，被试选择“不再提醒”或“继续努力调整”：选前者则该段从头再播一次，第二部分余下的播放不再因转头中断，`reminder_off_*` 为 1 表示该段最后完整的一次观看是在关闭提醒后播放的（选择记入 `events.csv` 的 `part2_reminder_choice` 与 `session.json` 的 `part2_reminder_off`） |
| `ssq.csv` | `participant_id, measurement, GD, FA, HE, ES, DF, IS, SW, NA, CO, FH, BV, DO, DC, VE, SA, BU`；`measurement` 为 `S_pre` / `S_mid1`（第一部分后）/ `S_mid2`（休息后、第二部分前）/ `S_post`（v3 及以前为 `S_mid`）；每项 0–3；QA 跳过的 SSQ 不写行 |
| `headpose.csv` | `participant_id, part, trial_index, segment, t_s, video_frame, yaw_deg, pitch_deg, roll_deg`；每个显示帧一行；`segment` 第一部分为 `clip`（回放为 `replay`），第二部分为 `A` / `B`（因头部转动中断的观看为 `A_stopped` / `B_stopped`）；**第二部分的 yaw、pitch 相对该片段的参考方向** |
| `playback.csv` | `participant_id, part, trial_index, segment, file, frames_expected, frames_shown, frames_dropped, dt_mean_ms, dt_p99_ms`；自定节奏（循环）播放时 `frames_expected` 为观看时长 × 片段帧率，`frames_shown` 为显示过的（遍数, 帧号）组合数 |
| `postquestionnaire.json` | 事后问卷只有一道开放问题（反馈与不舒服的地方，选填）：`participant_id`、`comments`、`timestamp_utc` |
| `events.csv` | `t_utc, event, detail`；语言、清单加载（含清单文件 SHA-256）、缺失刺激清单、清单摘要、步骤开始结束、重定向、休息起止与时长、注视练习对准与每次结果、重评、回放、QA 跳过（`qa_skip*`）、播放异常、写盘与备份结果、结束页操作（退出、重新开始） |
| `session.json` 另含 | `flow`、`part_order`、`plan_format_version`、`data_contract_version`、`settings_source`、`stimuli_dir` |

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
- 观看时长：第二部分每段观看时长、循环次数、每段最大头部偏离，第一部分每段观看时长，只报告分布。
- 主要比较组 Holm 校正；头部偏离角与丢帧的分布（阈值未设定时只报告）。
