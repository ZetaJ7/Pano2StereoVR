# 刺激素材盘点（173 服务器只读盘点，2026-11-19）

范围：为 E5 VR 主观实验准备刺激，盘点 lab-gpu2（173）上的源数据、Ours 4K 管线、DeepSynthesis（代码目录拼写为 `DeepSythesis`）与 MotionParallax 已有输出、工具链，并给出逐项可用性结论。全程只读：没有在服务器上创建、修改、移动或删除任何文件，没有碰 `output/R1_rerun_r1b/_queue`，没有启动 GPU 任务。IO 只用 `ls`、`find -maxdepth`、`head`、小文件读取，每条序列只对 1 个文件做 `file`/`ffprobe`。

代码状态：主仓库 `/mnt/nvme1/Pano2stereo` HEAD `b8e087d`（2026-11-17，merge fix/seam-meridian）；worktree `/mnt/nvme1/Pano2stereo_wt_prealloc` HEAD `e58279d`（branch `feat/frame-buffer-prealloc-r1b`，在 `b8e087d` 之上只加 4K 帧缓冲复用；`git merge-base --is-ancestor b8e087d e58279d` 成立）。与刺激相关的历史提交：`9178990`（2026-11-13，左右眼改为物理顺序）、`a844777`（2026-11-14，无切点处跳过 pose-aware 修正）、`3c81123`（2026-11-14，MotionParallax 基线显式关闭 pose-aware）、`6e41b72`/`5872606`/`93a7349`/`419ca9b`/`16d4a8f`（2026-11-15～17，ERP 接缝与像素中心修正）。

标记约定：「未核实」= 本次没有找到证据或没有检查；「推定」= 由代码或命名推得、没有实测文件。

---

## 1. 源数据

### 1.1 Replica（虚拟室内，论文所用）

论文所用 8 条序列即 StdTest 的 `replica_gen`：`/mnt/nvme1/Pano2stereo/Data/StdTest/replica_gen/<seq>/`（`Data` 是指向 `/home/ainot02/Dataset/PanoData` 的软链接；表 III 的运行日志里同一数据写作 `/mnt/disk2/Datasets/PanoData/StdTest/...`，两者推定为同一目录，未核实）。

| 序列 | 帧数 | 1080 版（StdTest `rgb`） | 原生 2K 渲染 | 原生 4K 渲染 |
| --- | ---: | --- | --- | --- |
| apartment_0_seed_300 | 300 | 2184×1092 PNG | 2884×1442 | 5768×2884 |
| hotel_0_seed_300 | 300 | 同上 | 同上 | 同上 |
| office_0_seed_300 | 300 | 同上 | 同上 | 同上 |
| office_1_seed_900 | 300 | 同上 | 同上 | 同上 |
| office_4_seed_300 | 300 | 同上 | 同上 | 同上 |
| room_0_seed_300 | 300 | 同上 | 同上 | 同上 |
| room_1_seed_600 | 300 | 同上 | 同上 | 同上 |
| room_2_seed_300 | 300 | 同上 | 同上 | 同上 |

- 原生 4K：`Data/Replica_gen/run_0009_stdtest_native_4k_png_u16mm_stereo_clean_20260430/<scene>/<scene>_seq_000_seed_<seed>__semantic/{rgb,depth,rgb_left,rgb_right}`，8 条全部 `rgb=300 depth=300`。`file .../office_0.../rgb/000000.png` → `PNG image data, 5768 x 2884`。`rerender_info.json`：`src_meta` 指向 run_0007 的同一条 seq_000 轨迹，`sensor_override {erp_height 2884, erp_width 5768}`，`max_frames 300`，`frame_step 1`，即与 StdTest 1080 版内容相同、分辨率不同。
- 原生 2K：`run_0008_stdtest_native_2k_...`，`file` → `2884 x 1442`，300 帧。
- 帧率：run_0007 `meta.json` 中 `"frame_interval_s": 0.04`、`"sequence_seconds": 20.0`、`"frames": 500` → 25 fps；300 帧 = 12 s。
- 垂直覆盖：habitat ERP 传感器 2:1 全景；`StdTest/replica_gen/depth_ray_conversion_report.json` 统计到 `-75..-88` 纬度带，覆盖到极点附近。
- 立体参考：StdTest 的 `rgb_left/rgb_right` 是指向 `Data/StdTest_ods/replica_gen/<seq>/` 的 ODS 参考（2184×1092，300 帧，2026-11-13 起替换旧的固定眼位参考，见 `StdTest/replica_gen/README_REFERENCE.md`）。run_0009 里的 `rgb_left/rgb_right` 是旧的固定眼位渲染（推定，未逐一核对）。
- 更多轨迹：run_0007（1080，2184×1092）每个场景有 15 条轨迹（如 `office_0_seq_000_seed_300` … `office_0_seq_014_seed_4500`），每条 500 帧（20 s）；只有 seq_000 有 2K/4K 重渲染。网格在 `/mnt/nvme1/Datasets/Replica-Dataset/replica_v1/<scene>/habitat/mesh_semantic.ply`，渲染环境 `habitat` conda env 存在。
- 另有 `Data/Replica_HeadRotation/{apartment_0,office_0,room_1}`（2025-12，2184×1092，500 帧，头部转动轨迹），只有 1080。

### 1.2 HELVIPAD（真实室内/室外）

位置：`/mnt/disk2/Datasets/HELVIPAD/`（`/mnt/nvme1/Datasets/HELVIPAD/` 结构相同）。本地只下载了 `images_bottom`、`depth_maps`、`metadata.csv`，`depth_maps_augmented` 只有 test `20231206_REC_02_OUT` 与 val `20240124_REC_12_IN`；README 列出的 `images_top`、视差图、`LiDAR_pcd` 不在本地。

预处理版：`/mnt/nvme1/Datasets/HELVIPAD_2to1fix/`（`/mnt/disk2/Datasets/HELVIPAD_2to1fix/` 亦有），`preprocess_info.json`：`"input_resolution": "1920x512"`、`"output_resolution": "1932x966"`、`"band_height": 515`，带状图居中、上下补零，附 `mask_valid`。

**室内序列（`_IN`）共 7 条**（帧数在原版与 2to1fix 中一致）：

| split | 序列 | 帧数 |
| --- | --- | ---: |
| test | 20240120_REC_06_IN | 998 |
| test | 20240127_REC_01_IN | 1201 |
| val | 20240124_REC_12_IN | 1320 |
| train | 20240120_REC_05_IN | 1530 |
| train | 20240120_REC_07_IN | 998 |
| train | 20240124_REC_06_IN | 1429 |
| train | 20240124_REC_11_IN | 1000 |

注意 test `20240120_REC_06_IN` 与 train `20240124_REC_06_IN` 是两条不同序列，名称易混。

室外：test `20231206_REC_02_OUT`、`20240124_REC_03_OUT`、`20240127_REC_02_OUT`、`20240124_REC_08_NOUT`；val `20240124_REC_02_OUT`；train 12 条 `_OUT`（`20231206_REC_01_OUT`、`20240120_REC_02/03/04_OUT`、`20240121_REC_01…05_OUT`、`20240124_REC_01/04/05_OUT`）与 2 条 `_NOUT`（`20240124_REC_07_NOUT`、`20240124_REC_09_NOUT`）。

- 分辨率与格式：JPEG 逐帧，`file .../test/images_bottom/20240120_REC_06_IN/0001.jpg` → `1920x512`；2to1fix → `1932x966`。
- 垂直覆盖**不完整**：论文 PDF（服务器本地副本）原文 "After cropping unnecessary regions (sky, ground), the resulting dataset comprises 39,553 paired images … at a resolution of 1920 × 512 pixels"。512/1920×360° ≈ 96°，即约 ±48° 纬度；2to1fix 中 966 行里只有中间 515 行有内容，上下各约 42° 为补零。
- 帧率：论文原文 "two Ricoh Theta V 360° cameras … capturing equirectangular images at 30 fps, paired with a Ouster OS1-64 LiDAR … operating at 10 fps"。数据集帧的实际时间间隔（30 Hz 还是按 LiDAR 10 Hz 采样）**未核实**。
- StdTest（`Data/StdTest/helvipad/<split>/<rec>/{images,depth,mask}`，每条取前 300 帧，`frame_selection {start 0, count 300}`，1932×966）：val `REC_12_IN`、val `REC_02_OUT`、test `REC_06_IN`、test `REC_02_OUT`、test `REC_08_NOUT`。即 StdTest 只含 2 条室内（`REC_06_IN`、`REC_12_IN`），这就是之前"只找到两条室内"的来源；原始数据里有 7 条。

### 1.3 PAIR360（真实室外）

位置：`/mnt/disk2/Datasets/PAIR360/Traversal1/<scene>/<Sequence>/{stitching,depth}`（`/mnt/nvme1/Datasets/` 下已没有 PAIR360 目录；`configs/eval_sets/standard.json` 里仍写旧路径 `/mnt/nvme1/Datasets/PAIR360/...`）。`stitching` 为 PNG 逐帧，`file .../1stDormitory/Sequence0/stitching/0054.png` → `PNG image data, 7680 x 3840`。

| 场景/序列 | stitching 帧数 | depth 帧数 | 备注 |
| --- | ---: | ---: | --- |
| 1stDormitory/Sequence0 | 1986 | 1 | depth 不完整 |
| 1stDormitory/Sequence1 | 250 | 250 | StdTest |
| 2ndDormitory/Sequence0 | 2819 | 2819 | StdTest |
| CentralLibrary/Sequence0 | 无 | 6447 | 无 RGB |
| CollegeofEngineering/Sequence0 | 2595 | 2595 | StdTest（窗口 start 1147，count 300） |
| CollegeofEngineering/Sequence1–3 | 339 / 270 / 889 | 同 | |
| CollegeofLifeScience/Seq0 | 无 | 无 | 空目录 |
| CollegeofLifeScience/Sequence0 | 629 | 628 | StdTest |
| CollegeofLifeScience/Sequence1–4 | 1617 / 1557 / 1327 / 2874 | 同 | |
| CollegeofPhysicalEducation/Sequence0 | 1116 | 1116 | StdTest |
| CollegeofPhysicalEducation/Sequence1–2 | 260 / 811 | 同 | |
| ParkingLotPeaceAmphitheater/Sequence0 | 无 | 5459 | 无 RGB |
| ParkingLotPeaceAmphitheater/Sequence1 | 3742 | 1515 | StdTest；另有 `_aligned_fix` 版 |

- StdTest：`Data/StdTest/pair360/<scene>_<seq>/{rgb,depth,segmentation}`，6 条（1stDormitory_Sequence1 250 帧，其余 300 帧），7680×3840。ParkingLot_Sequence1 有两个 RGB 版本：`StdTest/pair360/ParkingLotPeaceAmphitheater_Sequence1`（3 月）与 `..._aligned_fix`（6 月，首帧取 stitching `0611.png`），首帧文件大小不同（26471918 vs 26527738 字节），内容不同。
- 4K 下采样版：`Data/E4_PAIR360_4KDOWN/{1stDormitory_Sequence1,CollegeofEngineering_Sequence0,ParkingLotPeaceAmphitheater_Sequence1}`，`manifest.json`：`target_size [5768, 2884]`，RGB `cv2.INTER_AREA`，来源 `Data/StdTest/pair360`（非 aligned_fix 版）；`file .../rgb/00000.png` → `5768 x 2884`；帧数 250/300/300。
- 1080 版：`Data/E3_PARI360_1080DOWN`、`Data/E4_PAIR360_1080DOWN`，同样 3 条（2184×1092，由 DeepSynthesis 运行配置 `"resolution": "2184x1092"` 佐证）。
- 4K 视频：`output/P3_sched_freeze_20261004/inputs/pair360_collegeofengineering_seq0_3000f_5768x2884_concat10.mp4`，ffprobe：`codec_name=h264|width=5768|height=2884|pix_fmt=yuv420p|r_frame_rate=25/1|nb_frames=3000`，`duration=120.0`（300 帧片段循环 10 次，Table I 4K 输入）。
- 帧率：PAIR360 原生采集帧率**未核实**；项目内统一按 25 fps 编码。
- 垂直覆盖：2:1 ERP，推定为完整 180°；画面内是否有采集车辆或支架遮挡**未核实**。逐条序列的室内/室外属性**未逐一核实**（论文与本研究均按室外使用）。

### 1.4 其他

`Data/Insta360_4K_indoor.mp4`：ffprobe `mpeg4 3840x2160 30000/1001 fps 1183 帧 39.9 s`，16:9，是否为 ERP **未核实**。`Data/test1.mp4`（1920×1080，30 fps）、`Data/Avatar_Moving.mp4`（1920×1080，60 fps）同为非 2:1。

### 1.5 哪些源能原生达到 4K（每眼 5768×2884）

| 数据集 | 原生分辨率 | 4K 每眼 | 依据 |
| --- | --- | --- | --- |
| Replica | 渲染可任意；已有 5768×2884 × 8 条 × 300 帧 | 可以（已有） | run_0009 |
| PAIR360 | 7680×3840 | 可以（需下采样；3 条已有 4KDOWN） | `file` 与 4KDOWN manifest |
| HELVIPAD | 1920×512（约 96° 垂直带），补成 1932×966 | 不可以 | `preprocess_info.json`、论文原文 |

---

## 2. Ours 的 4K 管线

### 2.1 入口与"4K"设置

两条入口都支持同一组分辨率预设（`src/common/constants.py`）：

```
DOWNSAMPLE_RESOLUTIONS = {"720": (1456, 728), "1080": (2184, 1092), "2k": (2884, 1442), "4k": (5768, 2884)}
```

- 流式入口 `src/pano2stereo.py`：`--downsample 4k`（或直接给 5768×2884 输入）、`--experiment-mode {0,1,2,3}`（0 Passthrough，1 Baseline/Mono，2 Pose-agnostic，3 Pose-aware）、`--output-method` 只有 `rtsp`、`--rtsp-encoder`（帮助文字："H.264 NVENC is limited to 4096 px width, so the SBS outputs need hevc_nvenc"）。默认配置 `configs/pano.yaml`：`ipd 0.063`、`pixel 4448`、`critical_depth 100.0`、`repair_method hachaj_fast_gpu`、`gaze_vertical_disparity_correction.enable true`、`gaze_forward_unit [1.0, 0.0, 0.0]`、`experiment.initial_mode 3`、`depth_provider da2`；DA² 配置自动选 `configs/infer.json`。**此入口不写帧文件**，只推 RTSP。
- 离线入口 `src/test_offline_video.py`（表 III 等质量实验用）：`--path <视频或图片目录>`、`--downsample 4k`、`--enable-gaze-vertical-disparity-correction`（默认开）/`--disable-gaze-vertical-disparity-correction`、`--gaze-forward-unit`、`--depth-calibration-json`、`--depth-update-interval`、`--depth-update-lag-intervals`、`--critical-depth`（默认 100）、`--fps`（图片目录默认 30）、`--stereo-only`、`--novideo`。`--downsample` 给定时一律 `cv2.resize(..., INTER_LINEAR)` 到预设尺寸（对 HELVIPAD 1932×966 会变成放大）；不给时宽高向上取整到 14 的倍数（1932×966 不变）。

### 2.2 Table I 的 4K 运行（核对）

`/mnt/disk4/Pano2stereo_r1/R1_rerun_r1c/s44/T/cap_4k/half_{0,1}/`：
- `half_0.meta.json`：`"worktree": "/mnt/nvme1/Pano2stereo_wt_prealloc"`、`"commit": "e58279d04b4a…"`、`"tag": "exp-r1c-20261118"`、`"input": ".../pair360_collegeofengineering_seq0_3000f_5768x2884_concat10.mp4"`。
- 条件：`mode2`（`--frame-buffer-reuse auto`）与 `mode3`（`--experiment-mode 3 --frame-buffer-reuse auto`），各 5 次，`max_frames 300`，`warmup_frames 25`，`repair_method hachaj_fast_gpu`。
- 命令（`trial_record.json`）：`/home/ainot02/anaconda3/envs/pano2stereo/bin/python /mnt/nvme1/Pano2stereo_wt_prealloc/src/pano2stereo.py --config .../config.yaml --provider da2 --target-url "" --gaze-udp-port 0 --experiment-mode 2 --save-interval 0 --benchmark --max-frames 300 --benchmark-warmup-frames 25 --no-realtime-sync --frame-buffer-reuse auto`。
- `run.log`：`[FrameReader] Started: 5768x2884, mode=async, fps=25.00, total_frames=3000`；`[DA-2] Forward captured as a CUDA graph (input (1, 3, 2884, 5768))`；`[StereoGenerator] Precomputing sphere coordinates for 5768x2884`；`critical_depth=44.60m`（由 `pixel 4448` 算出）；`RTSPStream: 0.00 fps`（`target_url` 为空，没有发布任何帧）。
- 结论：4K 设置的输入与每眼输出都是 **5768×2884**；该批运行是纯计时，不留帧。

### 2.3 输出格式与眼序

- 流式：`stereo = cp.concatenate([left_repaired, right_repaired], axis=1)`（`src/stereo_generator.py`）→ **左右并排 SBS，左眼在左**；4K SBS 宽 11536×2884（由代码推得，没有实测文件）。Mono 模式 `make_baseline_sbs_frame` 把同一帧复制到两眼。
- 离线：`stereo/left_%05d.png`、`right_%05d.png`（修补前，含空洞）、`left_repaired_%05d.png`、`right_repaired_%05d.png`（最终视图）、`stereo_%05d.png`（修补后 SBS），`videos/stereo.mp4` 由 `stereo_*.png` 经 ffmpeg 合成。
- 眼序：`9178990` 之后 `left` 为物理左眼（`stereo_generator.py` 注释："The view built as "left" is therefore seen from the physical right eye … left, right = right, left"），测试 `scripts/std_test/test_eye_order_convention.py`。早于 `9178990` 的输出左右眼对调。
- 研究端要求的刺激格式是上下排列、左眼在上（`StudyTools/README.md`），所以 Ours 输出都需要从 SBS 或分眼 PNG 重新排成上下排列。
- **5768×5768 上下排列与 4K 设置一致**：两路 5768×2884 叠放即 5768×5768。但服务器上没有任何已生成的 4K 上下排列文件可做 ffprobe 核对。

### 2.4 PA 与 PAw 的选择

本研究中 PA = pose-agnostic，PAw = pose-aware（`Agents_communicate.md`："pose-agnostic（PA）对 pose-aware（PAw）"）。
- 流式：`--experiment-mode 2`（PA）/ `3`（PAw），或 config `experiment.initial_mode`；pose-aware 同时依赖 `gaze_vertical_disparity_correction.enable: true` 与 `gaze_forward_unit`（默认 `[1,0,0]`），参数 `eccentricity_threshold_deg 15`、`eccentricity_falloff_deg 5`、`gaze_distance_m 10`。
- 离线：`--disable-gaze-vertical-disparity-correction`（PA）/ 默认或 `--enable-...`（PAw），`--gaze-forward-unit x,y,z`。表 III 的 Ours 行 `test_config.json`：`stereo.gaze_vertical_disparity_correction.enable = False`。
- 第二部分的参考方向（`reference_yaw_deg 0`）与 `gaze_forward_unit` 在 ERP 上的对应关系**未核实**。

### 2.5 已有的 4K 输出

在已查看的位置**没有找到 Ours 的 4K 立体帧或视频**：
- Table I 4K 运行：只有计时文件（见 2.2）。
- `output/R1_4K_prealloc_20261117/correctness/*/sbs/` 为空目录（`n=0`）；同目录 `cmp_AA_fresh_m3_on.json` 记录两次相同设置的 4K 运行 `"equal_frames": 0`、`"max_abs": 148`、`"mean_abs_mean": 0.000155`（同代码重复运行不逐位相同，供复现性参考）。
- `E4_1B_replica_multires_frames_20260415`（已归档到 `/mnt/disk3/...`）的 `4k/` 里只有输入 `rgb/depth`，不是立体输出，且为 4 月旧代码。

### 2.6 当前版本的已有 Ours 输出（非 4K）

| 输出 | 内容 | 版本 | 分辨率 | 帧 |
| --- | --- | --- | --- | --- |
| `/mnt/disk4/Pano2stereo_r1/R1_rerun_r1b/B1_tableIII_ours_ray/1080/pano2stereo/da2/async1/replica_gen/<seq>/stereo/` | Replica 8 条，DA²，pose-agnostic，深度逐帧更新（`interval_frames 1`，`lag 0`），序列级 GT 标定（`calibration_split sequence_level`，0.1–10 m），`critical_depth 100` | `b8e087d`（`r1b_run_meta.json`，tag `exp-r1b-20261117b`） | 2184×1092 | 每条 left/right/left_repaired/right_repaired/stereo 各 300 |
| `R1_rerun_r1b/B7_pose_aware/runs/da2/{off,on_default}/<seq>/stereo/` | Replica 8 条 PA/PAw 对照 | r1b | 2184×1092 | 每条只有 20 个文件（抽样帧），不连续 |
| `R1_rerun_r1b/A6_ray_replay_motion/da2_k{1,2,3}__{helvipad,pair360}__*/test/stereo/` | HELVIPAD 5 条、PAIR360 6 条 | `b8e087d` | 1932×966 / 2184×1092 | 每条 20 个文件（抽样帧） |
| `R1_rerun_r1b/A6_ray/s1/<seq>/{async1,k1}/stereo/` | Replica 8 条 | r1b | — | 每条 20 个文件 |

即：当前版本下只有 Replica 8 条的 1080 pose-agnostic 连续 300 帧；HELVIPAD、PAIR360 以及任何 PAw 都没有连续帧。

---

## 3. DeepSynthesis 与 MotionParallax

### 3.1 DeepSynthesis

- 官方代码：`/mnt/nvme1/Pano2stereo/submodules/DeepSythesis`（commit `d0601bf`，origin `crs4/DeepOmniStereoFromMonoIndoorPano`）；`ckpt/` 含 gated 网络权重与 `RealESRGAN_x2plus.pth`、`RealESRGAN_x4plus.pth`。官方 README 的 4K 流程是 `panoverse_strip.py` → `img2equi.py` → `aizoom.py --zoom-factor 4`（RealESRGAN 学习型超分，从 1024×512 到 4K）。
- 项目封装：`scripts/baseline/run_deepsythesis_baseline.py`、`run_deepsythesis_resident_sequence.py`、`deepsythesis_{infer,compose}_common.py`、`fit_deepsythesis_depth_calibration.py`。
- 环境：conda `deepomnistereo_bl`（`/home/ainot02/anaconda3/envs/deepomnistereo_bl/bin/python` 存在）；评测用 `pano2stereo`。`scripts/baseline/README.md` 写的 `deepsythesis_bl` 环境不在 `envs/` 列表中。
- 运行参数（`test_config.json`）：`samples 360`、`head_radius 0.1`、`ipd 0.063`、`padded true`、`blending false`、`model_input_resolution 1024x512`、深度用逐序列 `log_depth_power` 标定。X2 报告（`output/R1_followup_20261116/X2_deepsynthesis_geom/report.md`）：输入 2184×1092 Lanczos 缩到 1024×512，合成后 Bicubic 放大回输入尺寸；眼序为物理顺序；DS 自身几何的等效眼距 5.847 cm、眼位前移约 9.6 cm（与 ODS 不同）。
- 运行记录示例（`R1_batchB_20261113_nvme/deepsythesis_seqcal_ray_nr/nr_helvipad__val__20240124_REC_12_IN.log`）：`/home/ainot02/anaconda3/envs/pano2stereo/bin/python scripts/baseline/run_deepsythesis_baseline.py --manifest .../deepsythesis_stdtest_video.json --conda-env deepomnistereo_bl --samples 360 --head-radius 0.1 --ipd 0.063 --model-width 1024 --model-height 512 --runner-mode resident --depth-calibration-json .../calib_seqcal_ray/nr_xdata/deepsythesis/<seq>.json ...`。
- DS 深度标定文件只有 NR 集 5 条：`R1_batchB_20261113_nvme/calib_seqcal_ray/nr_xdata/deepsythesis/{helvipad__test__20231206_REC_02_OUT, helvipad__val__20240124_REC_12_IN, pair360__1stDormitory_Sequence1, pair360__CollegeofEngineering_Sequence0, pair360__ParkingLotPeaceAmphitheater_Sequence1}.json`（Replica 8 条的标定在 `deepsythesis_seqcal_ray` 运行中使用，文件位置未逐一核对）。

已有输出（`stereo/left_%05d.png`、`right_%05d.png`，分眼 PNG；每条 `raw/<帧>/compose/<帧>_model_input_{l,r}.png` 保留 1024×512 合成原图）：

| 序列 | 路径 | 分辨率 | 帧 |
| --- | --- | --- | ---: |
| Replica hotel_0、office_1、room_0、room_2 | `/mnt/nvme1/Pano2stereo/output/R1_batchB_20261113_nvme/deepsythesis_seqcal_ray/runs/full_<seq>/sequences/replica_gen__<seq>/stereo/` | 2184×1092 | 300 |
| Replica apartment_0、office_0、office_4、room_1 | `/mnt/disk3/Pano2stereo_archive/output/R1_batchB_20261113/B1_baselines/deepsythesis_seqcal_ray/runs/full_<seq>/...`（同目录下 hotel_0/office_1/room_0/room_2 为 ENOSPC 中断的残缺版，46/68/176/254 个文件，已由 nvme 版取代，见 `seed_and_quarantine_report.json`） | 2184×1092 | 300 |
| HELVIPAD test 20231206_REC_02_OUT | `.../deepsythesis_seqcal_ray_nr/runs/nr_helvipad__test__20231206_REC_02_OUT/sequences/.../stereo/` | 1932×966 | 300 |
| HELVIPAD val 20240124_REC_12_IN | `.../nr_helvipad__val__20240124_REC_12_IN/...` | 1932×966 | 300 |
| PAIR360 1stDormitory_Sequence1 | `.../nr_pair360__1stDormitory_Sequence1/...` | 2184×1092 | 250 |
| PAIR360 CollegeofEngineering_Sequence0 | `.../nr_pair360__CollegeofEngineering_Sequence0/...` | 2184×1092 | 300 |
| PAIR360 ParkingLotPeaceAmphitheater_Sequence1 | `.../nr_pair360__ParkingLotPeaceAmphitheater_Sequence1/...` | 2184×1092 | 300 |

（`...` = `/mnt/nvme1/Pano2stereo/output/R1_batchB_20261113_nvme/deepsythesis_seqcal_ray_nr/runs`。）PAIR360 输入为 `E3_PARI360_1080DOWN`（3 月，非 aligned_fix）。之前"DS 的 HELVIPAD/PAIR360 输出未找到"不成立：5 条 NR 集输出在上表。更早的 `output/DeepSythesis_BL/runs/`（replica3、replica5、e3_objective，2026-03～04）为 R1 之前的旧版本。

### 3.2 MotionParallax

- 官方代码：`/mnt/nvme1/Pano2stereo/submodules/MotionParallax/{VR-6dof_preprocessing,VR-6dof_viewer,6dof_demo_exe}`；实际官方预处理在**本地 Windows** 上运行：`D:\WorkSpace\MotionParallax_R1\`（`code\VR-6dof_preprocessing`，官方提交 `bfe3ed7`，`.m` 未改），MATLAB `F:\MATLAB2026\bin\matlab.exe`（R2026b Update 1，8 核），输出 `D:\WorkSpace\MotionParallax_R1\outputs\<name>\`。`timing_rest_hel_20240124_REC_12_IN_da2.json`：`"matlab_version": "26.2.0.3433840 (R2026b) Update 1"`、`"computer": "PCWIN64"`。
- 边缘模型：官方需要 `modelBsdsBig.mat`，已无法下载，本次运行用 `modelBsds.mat` 替代并有标记文件（`README_run.md` 第 4 节，"需要用户决定"）。
- 输入：173 上打包（`output/R1_batchB_20261113/B1_baselines/motionparallax_official/inputs/manifest.json`，该目录已归档到 `/mnt/disk3/Pano2stereo_archive/...`）：RGB 为 StdTest 1080 帧（Replica、PAIR360 2184×1092，HELVIPAD 1932×966），深度为 DA² 原始预测 + C0 序列标定（Replica 0.1–10 m，NR 集 0.1–100 m），按 Jump 8 位编码 `e = 0.3/d − 0.1` 写成 libx264 视频，25 fps，补 2 帧。
- 官方输出（layered representation）：`output/R1_followup_20261116/MP_layered_ods/clips/<name>/vid2viewer/`，ffprobe `hel_20240124_REC_12_IN_da2.mp4`：`codec_name=h264|width=2048|height=1024|pix_fmt=yuv420p|r_frame_rate=25/1|nb_frames=301`，`duration=12.04`；`_BG.png` 等为 2048×1024。
- ERP 立体渲染（173，Linux 复刻查看器）：`MP_layered_ods/tools/mp_ods_render.py`（`mp_ods_batch.py --stage render|eval`），环境 `pano2stereo`。`tools/README.md`：ODS 光线、`--decode-offset 0.1`、头在球心、`layers=3`、"Output 2184 x 1092, rendered directly from the 2048 x 1024 layers"；输出 `stereo/{left,right}_%05d.png`。`render_config.json` 中 `width`/`height` 为参数（HELVIPAD 1932×966），即可以按其他尺寸渲染（是否支持 5768×2884、耗时如何**未核实**）。眼序校验文件 `validation/b_eyeorder_sectors.json`。
- **深度范围限制**：`B1_baselines/motionparallax_official_res/report.md` 第 4 节原文："The representable depth is therefore 0.273–3.0 m, and anything farther becomes 3 m"；"PAIR360, DA² C0 depth: minimum 5.4–7.9 m per sequence, so every pixel decodes to 3 m, which is a constant depth"。manifest 中 3 m 以外有效像素占比：REPLICA 3.0%–13.0%，HELVIPAD `REC_12_IN` 29.1%、`REC_02_OUT` 71.8%，PAIR360 99.7%–99.9%。
- 官方循环到 `floor(Duration·FPS)−1` 且在 20 s 截止（`README_run.md`）。

已有 ERP 立体输出（`/mnt/nvme1/Pano2stereo/output/R1_followup_20261116/MP_layered_ods/runs_final/<name>/stereo/`，全部有 `RENDER_DONE`）：

| name | 分辨率（每眼） | 帧 |
| --- | --- | ---: |
| rep_{apartment_0_s300, hotel_0_s300, office_0_s300, office_1_s900, office_4_s300, room_0_s300, room_1_s600, room_2_s300}_da2 | 2184×1092 | 300（各 600 个文件） |
| hel_20231206_REC_02_OUT_da2 | 1932×966 | 300 |
| hel_20240124_REC_12_IN_da2 | 1932×966 | 300 |
| p360_1stDormitory_Sequence1_da2 | 2184×1092 | 250 |
| p360_CollegeofEngineering_Sequence0_da2 | 2184×1092 | 300 |
| p360_ParkingLotPeaceAmphitheater_Sequence1_da2 | 2184×1092 | 300 |

另有 `rep_apartment_0_s300_da2__mp4tex`（官方 RGB mp4 纹理参照版）。HELVIPAD 只有 `REC_12_IN` 一条室内（与已知一致）。更早的 `output/MotionParallax_BL/runs/`（2026-04，Linux 移植、160 px 求解网格）为旧版本。

---

## 4. 服务器上已有的主观实验素材

在 `/mnt/nvme1/Pano2stereo`、worktree、`/mnt/disk1..4`、`/home/ainot02`（`maxdepth 4`，排除 `_queue`、`.git`、`node_modules`、`anaconda3`）按名称 `*stimul*`、`*subjective*`、`*E5_*`、`*study*` 搜索，**没有找到主观实验刺激**。命中的 `output/E5_stream_decouple_20261001`（串流性能实验 e0/e1/p1）、`output/E5_astra_review_r1_20261001`（只有 `pending_overwrite.json` 与脚本）、`P3_sched_freeze_20261004/candidate_block_e5_round1.patch` 都与刺激无关。

与"近景"相关的线索：`configs/paper/e41b_replica_targeted_cases_20260416.json`（2026-04-16，10 个近处注视候选帧，分布在 hotel_0 帧 90/120、office_0 帧 90/110、room_2 帧 60/100、room_1 帧 50/70、office_4 帧 0/70，即 5 条序列）；对应渲染 `E4_1B_replica_casebook_20260416_neargaze_circle`、`E4_1B_replica_gaze_candidates_20260416_rendered` 已归档到 `/mnt/disk3/Pano2stereo_archive/output/`，为 4 月旧代码的单帧图，不是视频。

本地 `D:\WorkSpace\Pano2StereoVR_study\Stimuli`（只读查看）：38 个文件，每个约 26–28 KB；抽查 `vin_A__ours.mp4`、`near_A__paw.mp4`：`codec_name=h264|width=1024|height=1024|r_frame_rate=25/1|nb_frames=75`，`duration=3.0`（`Agents_communicate.md` 记录 38 个均为 1024×1024、H.264、25 fps、3 s 测试片）。文件：`dummy_indoor__{mono,ours}`、`dummy_outdoor__{mono,ours}`、`near_{A..F}__{pa,paw}`、`rin_{A,B,C}__{mono,ours}`、`rout_{A,B,C}__{mono,ours}`、`vin_{A,B,C}__{mono,ours}`、`train_indoor__ours`、`train_near__{pa,paw}`、`train_outdoor__mono`。没有任何 `__deepsynthesis`、`__motionparallax` 文件，也没有 `dummy_indoor__{deepsynthesis,motionparallax}`。全部是测试片，不是正式刺激。

---

## 5. 工具

173 上**没有系统 ffmpeg**（`which -a ffmpeg ffprobe` 无输出，`/usr/bin/ffmpeg`、`/usr/local/bin/ffmpeg` 不存在）。conda 环境中的 ffmpeg：

| 路径 | 版本 | libx265 | hevc_nvenc | v360 |
| --- | --- | --- | --- | --- |
| `/home/ainot02/anaconda3/envs/pano2stereo/bin/ffmpeg`（另有同目录 `ffprobe`） | 8.0.1 | 有 | 有 | 有 |
| `envs/da3/bin/ffmpeg`、`envs/vr6dof_bl/bin/ffmpeg` | 8.0.1 | 有 | 有 | 有 |
| `envs/lerobot_v060/bin/ffmpeg`、`envs/baseline_video_stitcher/bin/ffmpeg` | 8.1.2 | 有 | 有 | 有（另有 libplacebo） |

另有 `baseline_gpu_image_stitching`、`baseline_omnistitch`、`dynamiclut_da3`、`dynamiclut_dibr`、`lerobot`、`RoboTwin`、`wheel` 环境带 ffmpeg（未逐一测试）。各环境 `-encoders` 中 HEVC 还有 `hevc_qsv`、`hevc_vaapi`、`hevc_vulkan`。`pano2stereo` 环境 `ffmpeg -h filter=v360`：`interp … lanczos 4`、`in_stereo … sbs 1 / tb 2`、`out_stereo … sbs 1 / tb 2`、`w`/`h`（0–32767）、`yaw`/`pitch`。GPU：8 × RTX 4090。

本地 Windows：`D:\WorkSpace\ffmpeg\bin\ffmpeg.exe` 版本 `N-111422-g886c1ffb5d-20230712`，有 `libx265`、`hevc_nvenc`、`hevc_amf`、`hevc_mf`、`hevc_qsv`、`libkvazaar`、`v360`。

未核实：`v360` 在 `in_stereo=tb:out_stereo=tb` 时 `w`/`h` 指整帧还是单眼；`hevc_nvenc`（RTX 4090）对 5768×5768 的支持；Windows Media Foundation 与 Pico 串流对 5768×5768 HEVC 的解码能力（`docs/subjective_study.md` 已列为需实测）。

---

## 6. 逐项结论：可直接用 / 需转换 / 需重新生成

判定口径（按协调方给定的类别）：
- 可直接用：已有，分辨率、格式、长度（≥10 s 连续）都满足，只需复制；
- 需转换：已有，只需确定性后处理（v360 lanczos 放大、帧转视频编码、眼图重排、截取）；
- 需重新生成：缺失、分辨率无法只靠后处理补足、方法版本不对（Ours 早于 `b8e087d`/`e58279d` 视为版本不对）或长度不足。

目标格式：上下排列、左眼在上的 HEVC mp4；Ours/Mono 每眼 5768×2884（HELVIPAD 是否例外待定，见第 7 节）。长度：25 fps 下 10 s = 250 帧，30 fps 下 = 300 帧（帧率待定）。**没有任何一项属于"可直接用"**：所有已有输出都是 PNG 分眼或 SBS 帧，至少需要重排和编码。

### 6.1 按条件的总体结论

| 条件 | 结论 | 依据与所需 |
| --- | --- | --- |
| mono（4K） | 需转换 | 源帧存在：Replica 8 条 4K 原生（run_0009）；PAIR360 3 条 4KDOWN；其余 PAIR360 为 7680×3840，需先缩到 5768×2884（确定性缩放）；HELVIPAD 只有 1932×966。转换 = 同一帧上下复制 + 编码 + 截取 |
| ours（4K，DA² pose-agnostic） | 需重新生成 | 未找到任何 4K 立体帧；当前版本只有 Replica 8 条 1080 连续帧。代码（`b8e087d`/`e58279d`）、环境（`pano2stereo`）、DA² 权重（`models/da2`）、输入（Replica 4K、PAIR360 4KDOWN、HELVIPAD 1932×966）都在；序列级 DA² 标定 `R1_batchA_20261113/A3/calib_ray/C0/da2_sequence_calibration.json` 覆盖 StdTest 19 条（在 1080 上拟合，用于 4K 是否合适未核实） |
| deepsynthesis | 已有序列：需转换；其他：需重新生成 | 已有 13 条（Replica 8、HELVIPAD 2、PAIR360 3），每眼 2184×1092 或 1932×966（模型原生 1024×512）；转换 = v360 lanczos 放大 + 上下排列 + 编码 + 截取。新序列需：`deepomnistereo_bl` 环境（在）、官方代码与权重（在）、1080 输入帧、DS 深度标定（需用 GT 深度另行拟合；HELVIPAD `depth_maps`、PAIR360 `depth` 在） |
| motionparallax | 已有序列：需转换；其他：需重新生成 | 已有 13 条，同上；官方层 2048×1024，ERP 渲染每眼 2184×1092/1932×966。新序列需：173 上打包 DA² C0 深度 + RGB → 本地 Windows MATLAB R2026b 跑官方预处理（`D:\WorkSpace\MotionParallax_R1`，边缘模型为替代版）→ 传回 173 用 `mp_ods_render.py` 渲染 |
| pa / paw（第二部分） | 需重新生成 | 没有任何连续帧（B7 只有每条 20 个抽样文件，且为 1080）。代码与环境在；Replica 4K 输入只有 8 条 seq_000 轨迹，若需新轨迹需 habitat 重渲染（`habitat` 环境与 Replica 网格在，渲染脚本入口未核实） |

### 6.2 第一部分：候选源序列 × 条件

Replica（虚拟室内；每条 300 帧 @25 fps = 12 s，均 ≥10 s）：

| 候选序列 | mono | ours | deepsynthesis | motionparallax |
| --- | --- | --- | --- | --- |
| apartment_0_seed_300 | 需转换：run_0009 4K 300 帧 | 需重新生成：4K 无；1080 当前版（`b8e087d`）在 `B1_tableIII_ours_ray/.../apartment_0_seed_300` | 需转换：disk3 归档 `full_apartment_0_seed_300`，2184×1092，300 帧 | 需转换：`runs_final/rep_apartment_0_s300_da2`，2184×1092，300 帧 |
| hotel_0_seed_300 | 需转换：同上 | 需重新生成：同上 | 需转换：nvme `full_hotel_0_seed_300`，300 帧 | 需转换：`rep_hotel_0_s300_da2`，300 帧 |
| office_0_seed_300 | 需转换 | 需重新生成 | 需转换：disk3 `full_office_0_seed_300`，300 帧 | 需转换：`rep_office_0_s300_da2` |
| office_1_seed_900 | 需转换 | 需重新生成 | 需转换：nvme `full_office_1_seed_900` | 需转换：`rep_office_1_s900_da2` |
| office_4_seed_300 | 需转换 | 需重新生成 | 需转换：disk3 `full_office_4_seed_300` | 需转换：`rep_office_4_s300_da2` |
| room_0_seed_300 | 需转换 | 需重新生成 | 需转换：nvme `full_room_0_seed_300` | 需转换：`rep_room_0_s300_da2` |
| room_1_seed_600 | 需转换 | 需重新生成 | 需转换：disk3 `full_room_1_seed_600` | 需转换：`rep_room_1_s600_da2` |
| room_2_seed_300 | 需转换 | 需重新生成 | 需转换：nvme `full_room_2_seed_300` | 需转换：`rep_room_2_s300_da2` |
| run_0007 其他轨迹（seq_001…014，1080，500 帧） | 需重新生成（4K 需 habitat 重渲染） | 需重新生成 | 需重新生成 | 需重新生成 |

HELVIPAD 室内（每眼原生 1932×966，带内约 96° 垂直视场；帧率未定）：

| 候选序列 | mono | ours | deepsynthesis | motionparallax |
| --- | --- | --- | --- | --- |
| val 20240124_REC_12_IN（StdTest 前 300 帧） | 需转换：`Data/StdTest/helvipad/val/20240124_REC_12_IN/images`，1932×966，300 帧 | 需重新生成：当前版只有 A6 的 20 个抽样文件 | 需转换：`nr_helvipad__val__20240124_REC_12_IN`，1932×966，300 帧 | 需转换：`runs_final/hel_20240124_REC_12_IN_da2`，1932×966，300 帧；官方层 2048×1024，301 帧 |
| test 20240120_REC_06_IN（StdTest 前 300 帧） | 需转换：`Data/StdTest/helvipad/test/20240120_REC_06_IN/images` | 需重新生成：同上 | 需重新生成：无输出、无 DS 标定 | 需重新生成：无输出 |
| test 20240127_REC_01_IN（1201 帧） | 需转换：`HELVIPAD_2to1fix/test/images_bottom/...`（需先选窗口） | 需重新生成 | 需重新生成 | 需重新生成 |
| train 20240120_REC_05_IN / 20240120_REC_07_IN / 20240124_REC_06_IN / 20240124_REC_11_IN | 需转换：2to1fix 帧 | 需重新生成 | 需重新生成 | 需重新生成 |

PAIR360 室外（每条原生 7680×3840）：

| 候选序列 | mono | ours | deepsynthesis | motionparallax |
| --- | --- | --- | --- | --- |
| 1stDormitory_Sequence1（250 帧 = 25 fps 下恰好 10 s；30 fps 下 8.3 s） | 需转换：`E4_PAIR360_4KDOWN/1stDormitory_Sequence1/rgb`，5768×2884，250 帧 | 需重新生成：当前版只有 A6 抽样 | 需转换：`nr_pair360__1stDormitory_Sequence1`，2184×1092，250 帧 | 需转换：`p360_1stDormitory_Sequence1_da2`，2184×1092，250 帧（深度几乎全部饱和为 3 m） |
| CollegeofEngineering_Sequence0（StdTest 窗口 start 1147，300 帧） | 需转换：4KDOWN，300 帧 | 需重新生成 | 需转换：`nr_pair360__CollegeofEngineering_Sequence0`，300 帧 | 需转换：`p360_CollegeofEngineering_Sequence0_da2`，300 帧（同上） |
| ParkingLotPeaceAmphitheater_Sequence1（300 帧，非 aligned_fix 版） | 需转换：4KDOWN，300 帧 | 需重新生成 | 需转换：`nr_pair360__ParkingLotPeaceAmphitheater_Sequence1`，300 帧 | 需转换：`p360_ParkingLotPeaceAmphitheater_Sequence1_da2`，300 帧（同上） |
| 2ndDormitory_Sequence0 / CollegeofLifeScience_Sequence0 / CollegeofPhysicalEducation_Sequence0（StdTest 300 帧） | 需转换：StdTest 7680×3840 → 缩到 5768×2884 | 需重新生成 | 需重新生成 | 需重新生成 |
| 其他原始序列（见 1.3） | 需转换（先选窗口、缩放） | 需重新生成 | 需重新生成 | 需重新生成 |

即：三个单元各需 3 条序列 × 4 条件。DS 与 MP 已有输出恰好覆盖 Replica 8 条、PAIR360 3 条（1stDormitory_Sequence1、CollegeofEngineering_Sequence0、ParkingLotPeaceAmphitheater_Sequence1），HELVIPAD 室内只有 `REC_12_IN` 1 条；Ours 4K 全部需要生成。

### 6.3 dummy 与训练片段

| 片段 | 条件 | 结论 |
| --- | --- | --- |
| dummy_indoor（1 段 × 4 条件） | mono / ours / deepsynthesis / motionparallax | 源序列未指定。若取第一部分未选用的 Replica 序列：mono 需转换、ours 需重新生成、DS/MP 需转换；若取 HELVIPAD 其他室内：DS/MP 需重新生成 |
| train_indoor | ours | 需重新生成（任一来源的 4K Ours 都没有） |
| train_outdoor | mono | 需转换（PAIR360 源帧；4KDOWN 的 3 条若已用于正式组，则需另取序列并缩放） |
| train_near（第二部分练习） | pa、paw | 需重新生成 |

### 6.4 第二部分：6 段 Replica 近景 × PA / PAw

| 项 | 结论 | 所需 |
| --- | --- | --- |
| near_A…near_F × pa | 需重新生成 | 4K 输入：Replica run_0009 只有 8 条 seq_000 轨迹，其中 5 条含 4 月近处注视候选帧；若与第一部分 Replica 片段不重叠或需要新的近景轨迹，需 habitat 重渲染 4K 源帧 |
| near_A…near_F × paw | 需重新生成 | 同上；另需确定 `gaze_forward_unit` 与研究端参考方向 `reference_yaw_deg 0` 的对应 |

### 6.5 本地 `Stimuli/` 的 38 个文件

全部是 1024×1024、3 s 的桌面测试片，**不能作为正式刺激**；正式刺激全部需要按 6.1–6.4 生成。

---

## 7. 缺口与待决定

以下只列事实与可选做法，不做取舍。

1. **HELVIPAD 分辨率与视场**：原生 1920×512（约 ±48° 纬度），补零后每眼 1932×966，达不到 4K，上下各约 42° 为黑。`study_config.example.json` 的 note 写的是"HELVIPAD clips keep their native 1932x966 per view"，与"所有刺激 4K"的说法并存。可选：(a) HELVIPAD 四个条件都保持 1932×966；(b) 四个条件都用同一 v360 lanczos 放大到 5768×2884（Ours 也可在 1932×966 上运行后放大，或输入先放大再运行，两者不同）；(c) 其他。三个单元之间分辨率与视场不同，属于单元间差异。
2. **HELVIPAD 室内序列数**：原始数据有 7 条 `_IN`（test 2、val 1、train 4），StdTest 只收 2 条；DS 与 MP 只有 `REC_12_IN`。凑够 3 条需要为另外 2 条重新跑 DS（含 DS 深度标定拟合）与 MP（含 Windows MATLAB 预处理）。可选序列见 1.2；`test REC_06_IN` 已有 StdTest 300 帧与 DA² C0 标定。
3. **MotionParallax 深度编码上限 0.273–3.0 m**：PAIR360 三条 99.7%–99.9% 的有效像素超出，解码后为恒定 3 m；HELVIPAD `REC_12_IN` 29.1%；Replica 3%–13%。可选：照官方编码使用并在方法中说明；或改编码（偏离官方）；或其他。
4. **DS/MP 放大起点**：DS 现有 2184×1092 已是 1024×512 Bicubic 放大的结果，`raw/.../compose/*_model_input_{l,r}.png` 保留 1024×512 原图；MP 现有 2184×1092 是从 2048×1024 层渲染的 ODS，渲染器可按参数设定输出尺寸。可选：从现有 2184×1092 放大；从 DS 1024×512 原图放大；MP 直接以 5768×2884 渲染（另需确认可行性与耗时）。DS 官方 README 的 4K 流程用 RealESRGAN（`aizoom.py`），与已定的确定性插值不同，仅作记录。
5. **Ours 4K 的生成方式**：服务器上没有任何 4K Ours 帧。需选定：离线入口（`test_offline_video.py`，表 III 口径：深度逐帧、序列级 GT 标定、`critical_depth 100`）还是流式入口（`pano2stereo.py`，只推 RTSP，DA² `fixed_scale 15`、`critical_depth` 由 `pixel 4448` 算出 44.6 m、异步深度）；标定是否沿用 1080 上拟合的 C0；4K 下 DA² 以 5768×2884 为输入（日志 `input (1, 3, 2884, 5768)`），与 1080 结果是否一致未核实；同代码重复运行不逐位相同（`cmp_AA_fresh_m3_on.json`）。
6. **帧率与片段长度**：Replica 25 fps；HELVIPAD 相机 30 fps、数据集帧间隔未核实；PAIR360 未核实；项目内 MP 打包用 25 fps，离线入口对图片目录默认 30 fps。10 s 对应 250 或 300 帧；PAIR360 1stDormitory_Sequence1 只有 250 帧。
7. **5768×5768**：与 `--downsample 4k`（每眼 5768×2884）一致，已由代码与日志核实；但没有实际文件，编码器、`v360` 上下排列参数语义、Windows/Pico 解码能力均未核实。
8. **眼图排列**：管线输出为 SBS（左眼在左）或分眼 PNG，研究端要求上下排列、左眼在上，需要统一重排；早于 `9178990` 的任何输出左右眼对调，不应使用。
9. **ParkingLot_Sequence1 两个 RGB 版本**：4KDOWN、DS、MP 的输入均来自 3 月非 aligned_fix 版（MP 输入的具体来源版本未核实）；若改用 aligned_fix 版，四个条件需统一。
10. **第二部分近景片段**：6 段 Replica 近景没有 PA/PAw 连续帧；4 月候选（5 条序列、10 帧）为单帧选择，不是 10 s 片段；与第一部分 Replica 片段是否允许重叠、是否需要新轨迹（需 habitat 4K 重渲染）待定。
11. **dummy 与训练片段的源序列**未指定（见 6.3）。
12. **MotionParallax 边缘模型**为替代版（`modelBsds.mat` 代 `modelBsdsBig.mat`），README 标为需用户决定；已有 13 条 MP 输出均在此条件下生成。
13. **存储**：4K 原始帧每帧 49,904,736 字节（日志 `frame=49904736 bytes`）；生成大量 4K PNG 时需留意 nvme1 余量与 HDD 归档（服务器当前负载高）。

---

## 8. 未核实清单

- HELVIPAD 数据集帧的实际时间间隔；PAIR360 原生帧率。
- PAIR360 画面内遮挡（采集平台）与各序列室内/室外属性。
- run_0009 中 `rgb_left/rgb_right` 是否为旧固定眼位参考（推定是）。
- `Data`（`/home/ainot02/Dataset/PanoData`）与 `/mnt/disk2/Datasets/PanoData` 是否为同一目录（推定是）。
- 4K SBS 宽 11536（由代码推得）。
- `mp_ods_render.py` 以 5768×2884 渲染的可行性与耗时。
- Replica 8 条 DS 标定文件位置；MP NR 集 PAIR360 RGB 的来源版本。
- `v360` 上下排列时 `w`/`h` 语义；RTX 4090 `hevc_nvenc`、Windows Media Foundation、Pico 串流对 5768×5768 HEVC 的支持。
- `gaze_forward_unit` 与研究端 `reference_yaw_deg` 的对应关系。
- C0 标定（1080 拟合）用于 4K 时的适用性。
- habitat 重渲染 4K 新轨迹的脚本入口与耗时。
- `Insta360_4K_indoor.mp4`（3840×2160）是否为 ERP。
