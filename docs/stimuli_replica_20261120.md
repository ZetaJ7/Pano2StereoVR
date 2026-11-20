# 正式刺激生成记录：Replica 第一部分、4 条新 Replica 序列、真实片段（插帧 C 版）与预览（2026-11-20）

依据：`stimulus_plan_20261119.md`（D1–D13）、`stimulus_inventory_20261119.md`、用户 11-20 的决定（去转向前方视野、真实片段用插帧 C 版、确认启动真实片段渲染与编码、本机 MATLAB、MP 传回渲染编码）。173 上的详细记录：`/mnt/disk4/Pano2stereo_r1/E5_stimuli/final/README.md`；清单 `final/manifest.json`；中间产物 `final/INTERMEDIATES.txt`。预览与前方视野数据：本机 `docs\previews_20261120\`（`index.md`）。

本文只报数和给依据，判定由用户确认。

## 1. 已生成

正式目录 173 `/mnt/disk4/Pano2stereo_r1/E5_stimuli/final/Stimuli/`（结构同计划第 9 节）。

| 位置 | 文件 | 状态 |
| --- | --- | --- |
| `part1/real_indoor/` | `rin_{A,B,C}__{mono,ours,deepsynthesis}.mp4`（A = HELVIPAD REC12IN s250，B = REC06IN s300，C = REC01IN s510） | 已编码（9 个） |
| `part1/real_outdoor/` | `rout_C__{mono,ours,deepsynthesis}.mp4`（PAIR360 1stDorm s120） | 已编码（3 个） |
| `part1/real_outdoor/_pending_window/` | `rout_A__<条件>__{move_s1790,stop_s1550}.mp4`（CoE）、`rout_B__<条件>__{move_s3440,stop_s1110}.mp4`（Parking），条件 mono/ours/deepsynthesis | 已编码（12 个），窗口待用户定 |
| `part1/practice/` | `train_outdoor__mono.mp4`（PAIR360 PhysEd s280 有运动窗口，插帧 300 帧） | 已编码 |
| 真实片段 `__motionparallax` | 8 条 C 片段 | 进行中：本机 MATLAB 第 2–8 步逐条运行（约 6 min/条），完成一条即自动上传 173、渲染、编码；rin_A 已渲染，正在编码 |
| `part1/virtual_indoor/`、`part1/dummy/` | vin_A–C、dummy_indoor × 4 条件 | **暂停**：等用户选前方偏移（第 4 节）。Ours 4K 帧已全部渲染；`vin_A__mono`（offset 0）只作计时与校验，放在 `E5_stimuli_work/part1_replica_staging_offset0/` |
| `part2/` | — | 按要求未生成 |

4 条新 Replica 序列（apartment_1、frl_apartment_0、office_2、office_3）：4K 5768×2884、300 帧、25 fps、rgb + depth，固定朝向，173 `/mnt/disk4/Pano2stereo_r1/E5_stimuli_work/Replica_gen/run_e5_stdtest_native_4k_fixedheading_20261120/<scene>/<scene>_seq_000_seed_300__semantic/`。

## 2. 方法与参数

### 2.1 Ours（PA）

- 离线入口 `src/test_offline_video.py`，worktree `/mnt/nvme1/Pano2stereo_wt_prealloc`，commit `e58279d`（exp-r1c）。用一个包装脚本 `ours_offline_repaired_only.py` 原样运行，只是不写修补前的左右眼、SBS 与红青图（只留 `left_repaired_*`、`right_repaired_*`）；仓库代码未改。
- 设置取自表 III Ours DA² 的 `test_config.json`（`B1_tableIII_ours_ray`）：DA² raw + 序列级 GT 标定（`calib_ray_20261114/da2_sequence_calibration.json`，0.1–10 m）、深度逐帧更新（interval 1、lag 0）、IPD 0.063、`critical_depth 100`、`hachaj_fast_gpu`、`--disable-gaze-vertical-disparity-correction`；另加 `--downsample 4k`（run_0009 原生 4K 输入，同尺寸不缩放）、`--fps 25`。
- 真实片段：同样设置，标定换成管线在原生窗口上拟合的 DA² C0（0.1–100 m，论文 NR 口径），参数不变，只复制到插帧输入的序列键下。HELVIPAD 在 1932×966 渲染后放大（D1），PAIR360 直接用 5768×2884 插帧帧。

### 2.2 其他条件与共同步骤

- mono：Replica run_0009 4K `rgb`；HELVIPAD C 版插帧帧；PAIR360 5768 插帧帧。
- DeepSynthesis：Replica 用盘点 3.1 节的已有输出；真实片段用 `ds_runs/runs/e5C_*`，HELVIPAD 用 raw/256 重新标定的 `_gt256`。
- MotionParallax：Replica 用 `runs_final/rep_*_da2`；真实片段见 2.4。
- 放大：每眼单独 `v360=input=e:output=e:interp=lanczos:w=5768:h=2884`。
- 去转向（Replica）：run_0007 seq_000 的 `yaw_rad`，sign −1，5768 宽整像素循环平移，四个条件同一平移。
- 编码：与解码测试片完全相同的 `hevc_nvenc` 参数，帧经管道，左眼在上。

### 2.3 新 Replica 序列（D5/Q5）

- 生成器：`scripts/archive/dataset_legacy/Replica_gen.py`（habitat 环境，habitat-sim 0.3.3），与 run_0007 相同的参数：`walk_retarget` 0.5 m/s、`min_goal_distance_m` 3.0、`max_path_tries` 300（run_0007 的 meta 未记录，取 `configs/replica_gen.json`）、转向 60°/s、`path_window` 1、`yaw_max_speed_deg_s` 60、`view.mode path`、相机高 1.5 m、深度 0.1–10 m、u16 mm PNG、0.04 s/帧，12 s = 300 帧，seed 300（4 个场景都一次成功，未用重试种子）。这一步只取轨迹（512×256）。
- 固定朝向（D8）：`make_fixed_heading_meta.py` 保留位置，把每帧 `yaw_rad` 改成第 0 帧的值（apartment_1 −1.9504、frl_apartment_0 −0.0435、office_2 −0.4328、office_3 −1.8164 rad），原值存为 `yaw_rad_path`。
- 4K 渲染：`replica_rerender_from_meta.py --mesh-variant semantic --erp-height 2884 --erp-width 5768 --max-frames 300`（立体参考关闭），GPU 2–5 各一条。
- 轨迹：路径长 5.98 / 4.52 / 4.60 / 5.24 m；原地转向（静止）0 / 73 / 69 / 37 帧；路径朝向变化范围 34 / 172 / 243 / 144°。
- 注意：habitat 写的 `depth` 是立方体面平面深度，不是光线距离（StdTest 已换算为 `depth_ray`）。预览中的深度已按 `make_depth_ray.py` 的公式换算；原文件未改。

### 2.4 MotionParallax C（进行中）

本机 `e5\run_rest_e5.m`、`e5\run_rest_batch_e5.ps1`（由 `run_rest.m`、`run_rest_batch.ps1` 派生，只改目录与函数名，`-RgbSource improved_rgb -ExclusiveMatlab -MinFreeGB 10`）→ `e5\mp_rest_uploader.ps1` 上传到 173 `E5_stimuli_work/mp_c_vid2viewer/<name>/`（sha256 两端核对）→ 173 `queue_mp_c.sh`：`mp_ods_render.py`（论文参数：decode offset 0.1、头在球心、3 层、movie 纹理 = 无损输入帧、输出 1932×966 / 2184×1092、帧 0:300）→ 编码（放大规则同上；CoE、Parking 两套窗口进 `_pending_window/`）。

## 3. 核对

- ffprobe：25 个已编码文件均为 hevc Main、hvc1、5768×5768、yuv420p、level 6.0、25 fps、300 帧、12.0 s。
- 眼序（各方法抽 1 帧，12 个 30° 扇区，左眼相对右眼的水平位移，1092 宽）：Replica ODS 参考 +7.2 px；Ours 4K +7.2、DS +6.9、MP +7.0；HELVIPAD Ours +1.4、DS +1.1；PAIR360 DS +0.9。全部扇区与参考同号，即均为物理顺序（`previews_20261120\eye_order_check.json`）。
- 帧数：Ours 每次渲染 300 帧，`run_validity` success，0 失败；DS、MP 来源各 300 帧（编码脚本检查第 0–299 帧齐全且连续）。
- `gaze_forward_unit`：代码中 ERP 列 x 的经度为 ((x+0.5)/W·2−1)·180°，单位向量 (cos lat cos lon, cos lat sin lon, sin lat)，所以默认 [1,0,0] = ERP 中心列 = 播放器的 content yaw 0；content yaw θ（向右为正）、pitch p（向上为正）对应 (cos p cos θ, cos p sin θ, sin p)。hotel_0 第 0 帧实测（`gaze_test_hotel0_f0_v3.png`）：PAw − PA 的差异只出现在给定方向 25° 以内（默认方向：0–5° 为 0，5–25° 环内平均 |Δ| 0.02–0.84 灰度级，25° 以外为 0；给 (0.5, 0.866, 0) 时差异移到 content yaw +60° 周围）。这一帧上 PAw 与 PA 的像素差很小（只有 7422 个像素差值 > 2）。

## 4. 前方视野（用户 11-20 要求，待用户挑片段和偏移）

D = content yaw ±30°、pitch ±20° 窗口内光线深度的中位数；“D<1 m 比例 / 最长”为逐帧统计；C = 窗口内 1 s 画面变化。offset 0 = 以第 0 帧朝向为前方；offset M = 整段再加一个常数整像素平移，把路径朝向的圆周均值放到前方。完整表（含 0.75 m、1.5 m 阈值和 12 个方向的 15° 圆深度）见 `previews_20261120\index.md`。

| 片段 | 均值朝向 | offset 0：D 中位数 / D<1 m 比例 / 最长 (s) | offset M：同左 | 静止 (s) |
| --- | --- | --- | --- | --- |
| vin_A apartment_0 | +40.3° | 1.04 / 0.29 / 2.7 | 3.47 / 0.11 / 1.3 | 0.5 |
| vin_B office_1 | +8.9° | 1.56 / 0.00 / 0 | 1.47 / 0.25 / 3.0 | 3.0 |
| vin_C room_0 | −16.1° | 4.79 / 0 / 0 | 5.64 / 0 / 0 | 2.7 |
| dummy office_4 | −21.8° | 2.76 / 0.02 / 0.2 | 2.77 / 0 / 0 | 0 |
| near_A hotel_0 | +23.8° | 1.22 / 0 / 0 | 1.20 / 0.11 / 1.3 | 3.0 |
| near_B office_0 | +0.7° | 2.12 / 0.31 / 3.7 | 同左 | 2.3 |
| near_C room_1 | +89.2° | 1.53 / 0.22 / 2.6 | 2.88 / 0 / 0 | 0 |
| near_D room_2 | −28.2° | 1.40 / 0.37 / 4.0 | 2.81 / 0.05 / 0.6 | 3.0 |
| 新 apartment_1 | −6.1° | 3.82 / 0.05 / 0.6 | 3.80 / 0.09 / 1.1 | 0 |
| 新 frl_apartment_0 | −25.4° | 3.55 / 0 / 0 | 3.03 / 0 / 0 | 2.9 |
| 新 office_2 | −52.2° | 1.75 / 0.36 / 4.4 | 0.96 / 0.53 / 3.6 | 2.8 |
| 新 office_3 | −52.3° | 3.34 / 0.12 / 1.4 | 5.39 / 0 / 0 | 1.5 |

## 5. 计时

- 第一次 Ours 4K 渲染（vin_A apartment_0，GPU 0）：300 帧墙钟 273 s（含模型加载），逐帧流水线平均 350 ms（DA² 274 ms、立体合成 59 ms），其余为 PNG 写盘；修补后两眼 PNG 3.3 GB。其余 3 条为 248–317 s。
- 真实片段 Ours：HELVIPAD 1932×966 每条 45–48 s；PAIR360 5768 每条 384–511 s（与 habitat 渲染同时运行）。
- 新序列 4K habitat 渲染：4 条并行，每条约 48–53 min（约 10 s/帧）。
- 编码：每个文件 25–92 s（HELVIPAD 与 2184 宽来源含 v360 放大）；Replica mono 约 260 s（经 Python 管道逐帧平移）。
- train_outdoor 插帧：下采样 88 s + minterpolate 33 min。
- 前方视野分析每段约 30 s（新序列 4K 约 160–180 s）。
- 估计余量：第一部分 Replica 16 个编码约 30–60 min（一个 GPU）；第二部分 7 段 × 6 = 42 次 4K 渲染，约 5 min/次，单 GPU 约 3.5 h，3 个 GPU 约 1.2 h，另加 42 个编码约 1 h，Ours PNG 约 140 GB；新序列另需先拟合 DA² 标定（见第 6 节第 2 项）。MP C 剩余 7 条，本机约 6 min/条，加 173 渲染编码，预计 16:45 前后完成。

## 6. 待决定与未完成

1. 第一部分 Replica：用户选前方偏移（offset 0 或 M）与片段后，用 `tools_replica/queue_encode_part1_v2.sh <0|mean>` 编码（Ours 帧已在）。
2. 第二部分：4 条新序列的用途（train_indoor、near_E、near_F、train_near）与每段参考方向；PAw 的居中方式（计划 D8 是渲染前平移输入；也可以在参考方向给 `--gaze-forward-unit`，配合配置中的 `reference_yaw_deg`），未决定。新序列在 DA² 标定文件中没有条目，Ours 渲染前需按论文方法（C0、序列级 GT、0.1–10 m）为它们拟合标定，GT 需由平面深度换算成光线距离；属于按论文协议补做，需用户同意。
3. rout_A（CoE）、rout_B（Parking）窗口：用户定后从 `_pending_window/` 复制为正式文件名。
4. 码率：插帧后的 PAIR360 片段与 train_outdoor 达到 maxrate（约 200 Mb/s，每段约 300 MB）；HELVIPAD 的 DS 约 155 Mb/s；HELVIPAD mono/Ours 68–82 Mb/s；Replica mono 32 Mb/s。解码测试片为 44 Mb/s，已看过的样片为 73–118 Mb/s。编码参数按规格未改，需在电脑与 Pico 串流下实测 200 Mb/s 的片段是否丢帧。
5. 静止段：去转向或固定朝向后，原地转向的帧成为完全静止的画面（第 4 节“静止”列）；near_B 与新 office_2 都以约 2.5 s 正对 0.26 m 处墙面的静止画面结束。
6. 标定适用性：Replica 标定在 1080 预测上拟合，PAIR360 C0 在 2184 预测上拟合，用于 4K 输入（DA² 内部都缩到约 24 万像素）。这与表 I 的 4K 做法一致，但未单独核实。
7. MotionParallax C 的本机运行：第一次批处理 8 条全部在第一步失败。原因是 Claude 环境的 `NoDefaultCurrentDirectoryInExePath=1` 被 MATLAB 继承，`system()` 找不到当前目录下的 `triangle_orientations.exe`；去掉该变量后重跑正常。失败记录另存于 `outputs_e5\<name>\failed_attempt_20261120_1538_noCurDirExe\`。运行中本机空闲内存最低到 0.55 GB（`-MinFreeGB 10`，用户决定）。
8. 删除说明：诊断时删除了本人几分钟前建的本机临时测试目录 `C:\Users\admin\AppData\Local\Temp\tri_test_e5`（15 s 测试的面图 JPG）。服务器上没有删除任何文件。
9. 本次有 4 次启动被权限拦截（真实片段 Ours 队列、PhysEd 插帧、真实片段编码队列、本机 MATLAB），均在用户确认后由主会话启动，记录在 173 `logs_replica/launch_record.txt` 与本机 `outputs_e5\launch_record.txt`。

## 7. 中间产物（清理前列给用户批准）

见 173 `final/INTERMEDIATES.txt`。较大的有：`/mnt/nvme1/E5_stimuli_work/ours_pa`（17 GB，第一部分 Replica 编码前需保留）、`/mnt/nvme1/E5_stimuli_work/ours_pa_real`（48 GB）、`/mnt/disk4/Pano2stereo_r1/E5_stimuli_work/real_ours`（6.6 GB，含 PhysEd 帧）、新序列 4K 源（8.1 GB，第二部分需要，保留）。
