# 正式刺激生成记录：第一部分（Replica 与真实片段）、新 Replica 候选与预览（2026-11-20）

依据：`stimulus_plan_20261119.md`（D1–D13）、`stimulus_inventory_20261119.md` 与用户 11-20 的各项决定（见第 6 节）。173 上的详细记录在 `/mnt/disk4/Pano2stereo_r1/E5_stimuli/final/README.md`，清单为 `final/manifest.json`，中间产物列在 `final/INTERMEDIATES.txt`。预览与前方视野数据在本机 `docs\previews_20261120\`，索引为 `index.md`。

本文只报数和给依据，判定由用户确认。

## 1. 已生成

正式目录为 173 `/mnt/disk4/Pano2stereo_r1/E5_stimuli/final/Stimuli/`，结构同计划第 9 节。

| 位置 | 文件 | 状态 |
| --- | --- | --- |
| `part1/real_indoor/` | `rin_{A,B,C}__{mono,ours,deepsynthesis,motionparallax}.mp4`（A = HELVIPAD REC12IN s250，B = REC06IN s300，C = REC01IN s510；插帧 C 版） | 12 个，全部完成 |
| `part1/real_outdoor/` | `rout_{A,B,C}__<4 条件>.mp4`（A = PAIR360 CoE 有运动窗口 s1790，B = Parking 有运动窗口 s3440，C = 1stDorm s120） | 12 个，全部完成 |
| `part1/real_outdoor/_pending_window/` | CoE s1550、Parking s1110 停车段的 mono/ours/deepsynthesis（共 6 个，未选用） | 留待清理 |
| `part1/practice/` | `train_outdoor__mono.mp4`（PAIR360 PhysEd s280 有运动窗口，插帧 300 帧） | 完成 |
| `part1/virtual_indoor/`、`part1/dummy/` | vin_A–C、dummy_indoor × 4 条件，共 16 个，按用户选的偏移编码（vin_A、dummy = 路径朝向均值；vin_B、vin_C = 第 0 帧朝向） | 16 个，全部完成 |
| `part2/near/`、`part2/practice/`、`part1/practice/train_indoor__ours.mp4` | 正式组（用户 18:40）：near_A–F 与 train_near 各 6 个（PA + PAw θ 10–30°），train_indoor 1 个（PA）；明细见第 5 节 | 18:25 开始，预计 19:50 前后全部完成 |
| `part2/_alternates/` | 备用组（17:25）18 个：`near_A_ref0__*`（hotel_0，0°）、`near_C_room1_refm60__*`、`near_D_room2_ref30__*` | 已完成，保留作备选 |

新 Replica 候选（只有 rgb 与 depth，供用户挑第二部分与练习片段）都是 4K 5768×2884、300 帧、25 fps、固定朝向，位于 173 `/mnt/disk4/Pano2stereo_r1/E5_stimuli_work/Replica_gen/run_e5_stdtest_native_4k_fixedheading_20261120/`：
- 第 1 轮：apartment_1、frl_apartment_0、office_2（seed 300）、office_3。用户已排除前 3 条，office_3 只作备选。
- 第 2 轮：apartment_2（seed 300 无可行路径，生成器自动换成 seed 600）、frl_apartment_1–4、office_2 seed 600。frl_apartment_4 是我选的补充场景，因为未用过的场景只剩 frl_apartment_4 和 frl_apartment_5。
- 第 3 轮：已有 8 个场景的 run_0007 其他轨迹，先预筛，再选 12 s 窗口。

## 2. 方法与参数

### 2.1 Ours（PA）

- 用离线入口 `src/test_offline_video.py`，worktree `/mnt/nvme1/Pano2stereo_wt_prealloc`，commit `e58279d`（exp-r1c）。通过包装脚本 `ours_offline_repaired_only.py` 原样运行，只是不写修补前的两眼、SBS 与红青图，仓库代码未改。
- 设置取自表 III Ours DA² 的 `test_config.json`：DA² raw，序列级 GT 标定（0.1–10 m），深度逐帧更新（interval 1、lag 0），IPD 0.063，`critical_depth 100`，`hachaj_fast_gpu`，`--disable-gaze-vertical-disparity-correction`。另加 `--downsample 4k`（输入为原生 4K，同尺寸不缩放）和 `--fps 25`。
- 真实片段的设置相同，只是标定换成管线在原生窗口上拟合的 DA² C0（0.1–100 m），参数不变，只复制到插帧输入的序列键下。HELVIPAD 在 1932×966 渲染后放大，PAIR360 直接用 5768 插帧帧。

### 2.2 其他条件与共同步骤

- mono：Replica 用 run_0009 4K `rgb`；真实片段用插帧帧。
- DeepSynthesis：Replica 用已有的 2184×1092 输出；真实片段用 `ds_runs/runs/e5C_*`，其中 HELVIPAD 用 raw/256 重新标定的 `_gt256`。
- MotionParallax：
  - Replica 用 `runs_final/rep_*_da2`。
  - 真实片段先在本机跑官方第 2–8 步（`e5\run_rest_e5.m`，`improved_rgb`），由 `e5\mp_rest_uploader.ps1` 上传 173 并做 sha256 核对。
  - 之后在 173 上用 `mp_ods_render.py` 渲染，参数与论文相同：decode offset 0.1、头在球心、3 层、movie 纹理 = 无损输入帧、输出 1932×966 或 2184×1092。
- 放大：每眼单独做 `v360=input=e:output=e:interp=lanczos:w=5768:h=2884`。
- 去转向（只用于 Replica）：用 run_0007 seq_000 的 `yaw_rad`，sign −1，在 5768 宽下做整像素平移。`--offset mean` 时再整段加一个常数平移，使路径朝向的圆周均值位于 content yaw 0。
- 编码：参数与解码测试片完全相同（`hevc_nvenc`），左眼在上。用户已在头显确认约 200 Mb/s 的片段播放流畅，参数不改。

### 2.3 新 Replica 候选的生成

- 第 1、2 轮：
  - 生成器为 `scripts/archive/dataset_legacy/Replica_gen.py`（habitat，habitat-sim 0.3.3），参数与 run_0007 相同：`walk_retarget` 0.5 m/s、`min_goal_distance_m` 3.0、`max_path_tries` 300、转向 60°/s、`path_window` 1、`yaw_max_speed_deg_s` 60、`view.mode path`、相机高 1.5 m、深度 0.1–10 m、u16 mm PNG、0.04 s/帧。直接生成 12 s（300 帧），这一步只取轨迹。
  - 然后用 `make_fixed_heading_meta.py` 把每帧朝向固定为第 0 帧的路径朝向。
  - 最后用 `replica_rerender_from_meta.py` 做 4K 渲染（semantic 网格，5768×2884，立体参考关闭）。
- 第 3 轮：
  - 预筛：8 个场景 × 15 条 run_0007 轨迹（1080，500 帧），每条去转向后按 1 s 步长取 12 s 窗口。每个窗口算两种固定朝向（窗口起点朝向、窗口内路径朝向均值）下的前方视野数据、静止时长、1 s 画面变化，以及 12 个方向 15° 圆内的深度。
  - 排除：seq_000（即已用片段）不进入预筛。run_0007 中种子重复导致的 22 条相同轨迹也排除，其中 office_1 seq_001/002 与 room_1 seq_001 和已用的 seq_000 完全相同。
  - 候选筛选（只用于提出候选，不是取舍规则）：D<1 m 比例 ≤ 0.05，D<1 m 最长 ≤ 0.5 s，静止 ≤ 3 s，C 中位数 ≥ 10，近处内容 0.5–1.5 m。每个场景先取静止最少、再取近处内容最近的一个窗口。全部窗口的数据在 `previews_replica/prescreen_run0007_windows.csv`，入选与备选在 `prescreen_run0007_picks.json`。
  - 4K 渲染：用 `make_window_fixedheading_meta.py` 截取窗口并固定朝向（取值记录在 meta），再用同一渲染脚本渲染。深度 PNG 的压缩级别由 9 改为 3：数值无损、完全相同，只是写盘更快。
- habitat 写出的 `depth` 是立方体面平面深度。预览与检查中按 `make_depth_ray.py` 的公式换算成光线距离，原文件未改。

## 3. 核对

- ffprobe：已编码文件均为 hevc Main、hvc1、5768×5768、yuv420p、level 6.0、25 fps、300 帧、12.0 s（见 `manifest.json`）。
- 眼序：各方法各抽 1 帧，在 12 个 30° 扇区内测左眼相对右眼的水平位移（1092 宽）。Replica ODS 参考 +7.2 px；Ours 4K +7.2；DS +6.9；MP +7.0；HELVIPAD Ours +1.4、DS +1.1；PAIR360 DS +0.9。所有扇区都与参考同号，即均为物理顺序（`eye_order_check.json`）。
- `gaze_forward_unit`：默认值 [1,0,0] 对应 ERP 中心列，即 content yaw 0。content yaw θ（向右为正）、pitch p（向上为正）对应 (cos p cos θ, cos p sin θ, sin p)。单帧实测中，PAw − PA 的差异只出现在给定方向 25° 以内（`gaze_test_hotel0_f0_v3.png`）。
- 渲染空洞：检查 4K 源帧的全部 300 帧，统计纯黑 rgb 比例与深度为 0 的比例，两者在所有片段中位置一致，说明是网格空洞。各片段的最大值：
  - 有明显空洞：apartment_1 21.2%，frl_apartment_0 36.0%，office_3 6.25%（300 帧都有），office_2 seed 300 2.0%。
  - 很小：office_0 0.12%，apartment_0 0.05%（用户认为两者都可接受）。
  - 其余已有片段不超过 0.005%。
  - 第 2 轮：frl_apartment_1–4 每帧 37–39%，apartment_2 最大 12%（平均 3.6%）。第 3 轮 8 个窗口的深度 0 比例都不超过 0.03%。office_0 seq_011 的纯黑 rgb 最大 0.56%，但对应位置深度正常，是深色物体，不是空洞。
- 第二部分 PAw：渲染日志中 e_th 与 gaze_forward_unit 与设定一致（near_D (0.866, 0.5, 0)，near_C (0.5, −0.866, 0)，near_A (1, 0, 0)）。

## 4. 近处物体（用户 11-20 ~17:25 要求）

近处内容多为墙面时，PA 与 PAw 的差别不易看出。为此对每个候选的 12 个方向（15° 圆）统计“物体像素”比例：深度 0.5–2 m，且 3.5° 内有至少远 1.3 倍的表面，即位于更远背景前的边缘。按最佳方向排序的完整表在 `index.md`；每个候选的方向条形图与 30° 区域截图在 `objects\`。

没有空洞的候选中，物体比例较高的依次为：
1. hotel_0 seq_008 s200（备选）：0.089，方向 −120°
2. near_A hotel_0：0.068，方向 +60°；用户选的 0° 方向为 0.040
3. hotel_0 seq_007 s000（入选）：0.068，方向 −30°
4. office_0 seq_002 s075（备选）：0.052
5. office_0 seq_011 s100（入选）：0.044，方向 +30°

room 系列较低：near_C 0.006，near_D 0.024，第 3 轮 room 窗口 0.003–0.011。

## 5. 第二部分正式组（用户 11-20 ~18:40）

content yaw 向右为正，0 为 ERP 中心，即片段的固定朝向（near_A 为去转向后第 0 帧的朝向）。pitch 均为 0。每段都有 PA 与 PAw θ = 10、15、20、25、30°（衰减 5°，gaze_forward_unit 指向参考方向）。

| clip | 来源（帧） | 固定朝向（habitat 世界 yaw，rad） | 参考方向 | 标定（论文方法，逐条） |
| --- | --- | --- | --- | --- |
| near_A | hotel_0 seq_000（0–299），渲染前输入去转向 | 第 0 帧朝向（−0.0866） | +60° | 论文表 III（`hotel_0_seed_300`） |
| near_B | hotel_0 seq_008 seed 3300（200–499） | −1.3305（窗口路径朝向均值） | −120° | `calib_e5/fits/hotel_0_seed_3300.json` |
| near_C | office_0 seq_002 seed 900（75–374） | 2.5235（均值） | −60° | `office_0_seed_900.json` |
| near_D | office_0 seq_011 seed 3600（100–399） | −1.4525（均值） | +30° | `office_0_seed_3600.json` |
| near_E | hotel_0 seq_007 seed 2400（0–299） | −1.5616（起点） | −30° | `hotel_0_seed_2400.json` |
| near_F | apartment_0 seq_007 seed 2400（0–299） | 2.9998（起点） | −30° | `apartment_0_seed_2400.json` |
| train_near | apartment_0 seq_003 seed 1200（175–474） | 0.9291（起点） | −90° | `apartment_0_seed_1200.json` |
| train_indoor（第一部分练习，只有 PA） | room_0 seq_003 seed 1200（25–324） | 1.4983（起点） | — | `room_0_seed_1200.json` |

研究配置中的 `part2.clips[].reference_yaw_deg` 应改为上表的参考方向，train_near 为 −90，由主会话更新。

## 6. 前方视野

在 content yaw ±30°、pitch ±20° 的窗口内逐帧计算光线深度中位数 D；统计 D<1 m 的帧比例与最长连续时长；C 为该窗口内的 1 s 画面变化。完整数据见 `previews_20261120\index.md`（12 个候选方向的 15° 圆深度、两种偏移、三个阈值、静止时长）。第一部分 4 段的偏移已由用户选定（见第 1 节）。

## 7. 计时

- Ours 4K（Replica，300 帧）：每次墙钟 248–317 s，其中第一次为 273 s（含模型加载）。逐帧流水线平均 289–362 ms（DA² 约 230–300 ms），其余时间为 PNG 写盘。两眼 PNG 共 3.3–5.7 GB。
- Ours 真实片段：HELVIPAD 每条 45–48 s；PAIR360 5768 每条 384–511 s。
- habitat 4K 渲染：深度 PNG 用 level 9 时约 10 s/帧（每条约 50 min）。第 3 轮改用 level 3，耗时见 `logs_replica/round3.log`。
- 编码：每个文件 25–92 s（直接管道）。Replica 经 Python 逐帧平移，每个文件 240–400 s。
- MP 真实片段：本机官方第 2–8 步约 6 min/条，173 渲染约 3 min/条，编码约 80 s。
- train_outdoor 插帧：下采样 88 s，minterpolate 33 min。
- 估计余量：第二部分为 7 段 × 6 个变体 = 42 次 Ours 4K 渲染，约 5 min/次，3 个 GPU 并行约 1.2 h；另有编码约 1 h，Ours PNG 约 140 GB。

## 8. 用户 11-20 的决定（按时间）

1. 真实片段用插帧 C 版。
2. 确认启动真实片段的 Ours 渲染、PhysEd 插帧和编码队列，并启动本机 MATLAB；MP 结果传回 173 后渲染并编码。
3. 第一部分偏移：vin_A、dummy 用路径朝向均值，vin_B、vin_C 用第 0 帧朝向；2–3 s 的静止段可以接受。
4. CoE、Parking 用有运动窗口；高码率片段在头显中播放流畅，编码参数不改。
5. 排除 apartment_1、frl_apartment_0、office_2（seed 300）、office_0（seq_000，结尾正对墙角）。office_3 只作备选。空洞比例 0.12% 和 0.05% 可以接受。
6. 补充第 2 轮 6 个候选和第 3 轮（已有场景的新轨迹）候选。
7. 新候选的 DA² 标定与论文做法一致（逐条序列级拟合）。
8. 先渲染第二部分备用片段 near_A（hotel_0，0°）、near_C（room_1，−60°）、near_D（room_2，+30°），用 PA 与 PAw θ 10–30°。
9. 第二部分正式组见第 5 节，备用组移到 `_alternates/`。如果 Replica 不够，再启用 Blender rich_scenes_v3（已记入计划文档第 10 节）。

## 9. 待决定与未完成

1. 第二部分及两个练习片段（train_indoor、train_near）共需 8 段只用 Ours 的片段。可用的已有片段为 hotel_0、room_1、room_2；候选来自第 2、3 轮的预览。用途与参考方向由用户定。
2. 新候选的 DA² 标定：按论文方法逐条拟合（用户决定）。15 条候选在两个 GPU 上依次做 1080 渲染与 DA² 预测，全部完成后统一拟合，结果写入 `E5_stimuli_work/calib_e5/da2_sequence_calibration_e5.json`。参数见第 5 节和 `manifest.json`。
3. PAw 居中：备用片段采用渲染前平移输入（D8，offset 0），并把 `--gaze-forward-unit` 设为参考方向。研究配置中的 `reference_yaw_deg` 应为 near_A 0、near_C −60、near_D +30（由主会话更新）。
4. 标定适用性：标定是在 1080 或 2184 的预测上拟合的，现在用于 4K 输入。这与表 I 的 4K 做法一致，但未单独核实。
5. 过程记录：
   - 本机 MATLAB 第一次批处理失败。原因是 Claude 环境变量 `NoDefaultCurrentDirectoryInExePath=1` 被 MATLAB 继承，去掉该变量后正常。运行中本机空闲内存最低降到 0.55 GB。
   - 16:25:10–16:26:29 GPU 0 上同时有我的两个进程（rout_A 的 MP 编码与 habitat 渲染，抢同一块空闲 GPU 所致）。之后改为固定分配 GPU。
   - 预筛冒烟测试误写到 173 `/tmp/e5_prescreen_test_unused`（1.9 MB），已列入清理清单。
   - 诊断时删除了本机临时测试目录 `C:\Users\admin\AppData\Local\Temp\tri_test_e5`（本人 15 s 测试的输出）。173 上没有删除任何文件。
   - 4 次启动被权限拦截，都在用户确认后由主会话启动（记录在 `logs_replica/launch_record.txt`）。

## 10. 中间产物

见 173 `final/INTERMEDIATES.txt`。较大的有：
- `/mnt/nvme1/E5_stimuli_work/ours_pa`（17 GB）
- `/mnt/nvme1/E5_stimuli_work/ours_pa_real`（48 GB）
- `/mnt/disk4/Pano2stereo_r1/E5_stimuli_work/real_ours`（6.6 GB）
- 新 Replica 4K 源（每条约 2 GB；被排除的场景可在用户批准后清理）
- `_pending_window/` 中的 6 个停车段文件
