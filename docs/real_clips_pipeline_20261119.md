# 真实片段（HELVIPAD、PAIR360）的 DS / MP 刺激生成记录（2026-11-19 夜）

完整记录在 173：`/mnt/disk4/Pano2stereo_r1/E5_stimuli/README_real_clips.md`（命令、脚本、差异、决定事项）；逐条清单 `manifest_real_clips.json`（每 10 分钟自动刷新）。本文是中文摘要。服务器根目录下文记为 `$E`。

## 1. 片段

| # | clip | 来源 | start | 窗口内 GT 深度帧 |
| --- | --- | --- | ---: | ---: |
| 1 | `hel_REC12IN_s250` | HELVIPAD val `20240124_REC_12_IN` | 250 | 120 |
| 2 | `hel_REC06IN_s300` | HELVIPAD test `20240120_REC_06_IN` | 300 | 120 |
| 3 | `hel_REC01IN_s510` | HELVIPAD test `20240127_REC_01_IN` | 510 | 120 |
| 4 | `p360_1stDorm_s120` | PAIR360 `1stDormitory/Sequence1` | 120 | 120 |
| 5 | `p360_CoE_stop_s1550` | PAIR360 `CollegeofEngineering/Sequence0` | 1550 | 120 |
| 6 | `p360_Parking_stop_s1110` | PAIR360 `ParkingLotPeaceAmphitheater/Sequence1` | 1110 | 52 |
| 7 | `p360_CoE_move_s1790` | PAIR360 `CollegeofEngineering/Sequence0` | 1790 | 120 |
| 8 | `p360_Parking_move_s3440` | PAIR360 `ParkingLotPeaceAmphitheater/Sequence1` | 3440 | 47 |

变体：`e5B_<clip>` 原生 10 fps 120 帧；`e5C_<clip>` 插帧 25 fps 300 帧。

## 2. 产物位置（173）

| 内容 | 位置 |
| --- | --- |
| DS/MP/DA² 输入帧（HELVIPAD 1932×966，PAIR360 2184×1092） | `$E/frames/StdTest/{helvipad/<split>,pair360}/e5{B,C}_<clip>/{images,rgb}/` |
| PAIR360 5768×2884 原生帧 / 插帧结果（无损 PNG，供我们的方法） | `$E/frames/p360_5768/native/<clip>/`、`$E/frames/p360_5768/interp/<clip>/` |
| HELVIPAD 插帧结果（1932×966 无损 PNG） | `$E/frames/StdTest/helvipad/<split>/e5C_<clip>/images/` |
| DA² 原始深度 | `$E/da2_pred/e5{B,C}_<clip>/depth_npy/` |
| DA² C0 标定（原生窗口拟合，C 复用） | `$E/calib/da2/<clip>_da2_C0.json` |
| DS 标定（原生窗口拟合；C 用同参数副本 `_forC.json`） | `$E/calib/ds/` |
| MP 输入视频与 sha256 | `$E/mp_inputs/`、`$E/mp_inputs/manifest.json` |
| MP 结果（本机回传） | `$E/mp_outputs/<name>/`（`videos\`、timing json、`UPLOADED_sha256.txt`） |
| DS 结果 | `$E/ds_runs/runs/<name>/sequences/<seq>/stereo/` |

本机：脚本 `D:\WorkSpace\MotionParallax_R1\e5\`；MP 输入 `inputs_e5\`；MP 输出 `outputs_e5\<name>\`；状态 `outputs_e5\driver_status.json`、日志 `outputs_e5\driver_log.txt`。

## 3. 流程要点

1. **原生帧**：HELVIPAD 用原 JPEG（与论文 StdTest 同一批文件）；PAIR360 由 7680×3840 原图 `INTER_AREA` 到 2184×1092（同 `E4_PAIR360_1080DOWN`）和 5768×2884。
2. **插帧（C）**：方法同 `interp_test/make.sh`（minterpolate mci/aobmc/bidir/vsbmc=1/scd=none，左右循环补边约 10%：1932 宽 192 px，5768 宽 576 px，插后裁回）。输入窗口 120 帧加其后 2 帧，取前 300 帧（t = 0…11.96 s）。HELVIPAD 在 1932×966 上插；PAIR360 在 5768×2884 上插一次，再 `INTER_AREA` 到 2184×1092 给 DS、MP、DA²。
3. **DA²**：命令同论文 A3（`test_offline_video.py --postprocess-mode raw`）。B、C 各自推理。
4. **标定**：DA² 用 A3 C0 方法（A3 自己的 `_stats_one`、`Sums.solve`），DS 用论文 NR 的 `fit_deepsythesis_depth_calibration.py` 参数；都只在原生帧上拟合，C 复用同一组参数。Parking 只用有深度的帧：stop 52 帧，move 47 帧。
5. **MP 输入**：深度转换同 `make_mp_da2_nr_clips.py`，打包用 `make_official_inputs.py` 的副本（编码、补 2 帧、往返检查不变）。
6. **MP（本机）**：`e5_driver.ps1` 自动拉取输入、3 进程 × 2 线程只跑深度改进、先 B 后 C、完成后回传并核对 sha256。
7. **DS（173）**：设置同论文 NR 运行（samples 360、head_radius 0.1、ipd 0.063、1024×512、resident），一次一个 GPU 进程，只用 GPU 0–3。

## 4. 与论文协议的差异（已执行）

1. PAIR360 的 2184×1092 帧按任务要求用 INTER_AREA，DA²、DS、MP 共用；论文中 DA² 和 MP 用的是 INTER_LINEAR，DS 用 INTER_AREA。
2. 插帧多用 2 个窗口外原生帧，以得到 300 帧（make.sh 原样只出 296 帧）。
3. 5768 插帧加 FFmpeg 全局选项 `-max_alloc`（默认单次分配上限导致 ENOMEM），滤镜不变。
4. DS 运行不做 NR 评测（不影响立体帧）。
5. Parking 标定只用有深度的帧，RGB 与深度按文件名对齐。论文 StdTest 的 Parking 序列中 RGB 与深度没有对齐（RGB 00000 = 0607，depth 00000 = 1513），论文 Parking 的 DA² 和 DS 标定用的是那一套。
6. HELVIPAD 的 DS 运行排在 5 条 PAIR360 B 之后，只改变顺序。
7. HELVIPAD 的 DS 标定改用 raw/256 m（用户决定，11-20，第 5.1 节）。
8. DS 并行 6 个进程（用户决定，11-20）。

## 5. 决定事项

### 5.1 用户已决定（11-20 上午）
1. **HELVIPAD 的 DS 改用 raw/256 m**（偏离论文 DS 标定的 raw/1000）。参数取自 `$E/calib/ds_alt/`（同一缓存、同样帧与采样；它按 raw/1000 重算与原拟合逐位相同），写成 `$E/calib/ds/hel_*_deepsythesis_gt256.json` 与 `_gt256_forC.json`，读回核对与 `ds_alt` 完全相等：REC12IN 0.274818/1.754717，REC06IN 0.290889/1.440044，REC01IN 0.338090/1.216992。6 个 HELVIPAD DS 作业改用新文件，输出到新目录 `$E/ds_runs/runs/e5{B,C}_<clip>_gt256`；旧尺度的 3 帧（`e5B_hel_REC12IN_s250`）保留不用。其余 HELVIPAD DS 从未以旧尺度运行（07:08 放行时队列已在跑 PAIR360 C，08:48 重新扣住）。
2. **其他读 HELVIPAD GT 的步骤都已是 raw/256**：DA² C0 拟合用 `png16_mm`×3.90625，HELVIPAD 的 MP 输入、MATLAB 结果和我们方法要用的 DA² C0 标定都有效。按 /1000 读的只有 DS 标定这一步，已被替代；没有发现别的。
3. **DS 并行 6 个进程**：新队列 `gpu_queue4.sh`（GPU 0–3，DS 要求该卡空闲 ≥ 6 GB），08:54 起 6 个并行，实测 35–40 s/帧。

### 5.2 仍待决定
1. **MP 深度的 uint16 回绕**：论文 NR 片段的毫米转换对 > 65.535 m 的深度取模，(65.536, 68.536) m 的像素被解成 3 m 以内的近处，在论文 PAIR360 片段中占 0.1–0.3%，且几乎就是 PAIR360 中全部“近于 3 m”的像素。本次 MATLAB 按论文方式（回绕）运行；不回绕的备选输入在 `$E/mp_inputs_nowrap/`，未运行。未改动，等论文层面的核查。

## 6. 进度（11-20 08:55）与预计时间

- MP：16 条中 14 条已完成并回传（`$E/mp_outputs/`，sha256 两端一致）；`e5C_p360_CoE_move_s1790`、`e5C_p360_Parking_move_s3440` 在跑，预计 10:10、11:15 完成。
- DS：PAIR360 B 5 条已完成；1stDorm C 189/300；并行中 CoE_stop C、Parking_stop C、HELVIPAD B 3 条。预计（40 s/帧）：HELVIPAD B 约 10:15，CoE_stop C、Parking_stop C 约 12:15，CoE_move C、Parking_move C、HELVIPAD C 1–2 约 13:40，HELVIPAD C 3 约 15:30；全部约 11-20 15:30，若论文队列恢复满载可能到 20:00 前后。
- 日志：173 `$E/logs_real/gpu_queue4.log`、`chain_{B,C}_<clip>*.log`；本机 `D:\WorkSpace\MotionParallax_R1\outputs_e5\driver_log.txt`。173 上的状态清单每 10 分钟刷新。

## 7. 未完成 / 未核实

未完成：
- DS 10 条在跑或排队（见上）。
- `mp_inputs_nowrap/`：B 3 条已建，其余由 `nowrap_all2.sh` 自动补齐；不在 MATLAB 上运行。
- ERP 渲染不在本次任务内。

未核实：
- MP、DS 输出画面未目检，只按 `DONE.json`、帧数和 sha256 判定。
- 插帧结果未目检；只核对了时间对齐（C 第 0、5、10、295 帧与 B 第 0、2、4、118 帧一致，PSNR 约 50 dB），接缝处和大运动处的插值质量未看。
- DA² 在插帧帧上的时间稳定性、C 变体沿用原生窗口标定是否合适，未检查。
- HELVIPAD DS 尺度问题与 Parking 深度错位对论文已有结果（Table III NR 行等）的影响，未核实（论文层面另行核查）。
- HELVIPAD、PAIR360 为 10 Hz 的依据来自用户（stimulus_plan D11），本次未独立核实。
- 本机驱动依赖用户保持登录；注销会结束驱动和 MATLAB。
