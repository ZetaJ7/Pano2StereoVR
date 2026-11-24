# 主观实验刺激生成计划（草案，2026-11-19）

依据：`docs/stimulus_inventory_20261119.md`（173 只读盘点）与用户 11-19 的决定。标“待确认”的条目由用户决定后才执行。

## 0. 用户已决定（11-19）

| # | 决定 |
|---|---|
| D1 | HELVIPAD 只有约 96° 垂直视场，上下黑边保留（数据集如此）；真实室内一格四种方法都用同一种插值放大到 5768×2884 |
| D2 | HELVIPAD 室内缺的 DeepSynthesis、MotionParallax 重新跑，排好任务进度 |
| D3 | MotionParallax 的 0.273–3 m 深度上限按原样使用，主观实验章节写清楚 |
| D4 | 统一 25 fps |
| D5 | 每段用完整 12 s（300 帧）；Replica 不够的用 Replica_gen 再生成几条，尽量用不同场景 |
| D6 | Ours 用离线回放生成，所有方法都是每帧用对应帧的深度渲染；第一部分的 Ours 是 PA（其他方法没有 PAw 模式）；第二部分为 PA 对 PAw |
| D7 | 先生成 5768×5768 HEVC 解码测试片（已完成，见第 6 节） |
| 早先 | DeepSynthesis、MotionParallax 的原生输出统一用同一种可控插值放大到 4K（FFmpeg `v360`，lanczos） |
| D8 | Replica 片段去掉相机转向（用户 11-19，头显实测发现画面自身运动时转头很晕）：按轨迹真值 `yaw_rad` 逐帧做整像素水平循环平移（`tools/stimuli/yaw_stabilize.py`，方向 sign = −1 已验证）；第一部分对四种方法的输出做同一平移；第二部分（PAw 依赖固定前方）在渲染前平移输入的 rgb 与深度；新生成的 Replica 序列直接用固定朝向渲染。真实数据（HELVIPAD、PAIR360）没有朝向真值，不做 |
| D9 | 第一部分说明页加“画面本身在移动时，建议少做主动转头探索，以免头晕” |
| D10 | PAIR360 1stDormitory_Seq1 只有 250 帧，就用它的全部长度（用户 11-19） |
| D11 | 真实数据（HELVIPAD、PAIR360）原生 10 Hz（HELVIPAD arXiv 2411.18335 附录 A.1 与图 11；PAIR360 RA-L 2024 第 III-C 节），不采用按 25 fps 加速 2.5 倍播放；在“插帧到 25 fps”和“原生 10 fps 播放”之间选，先做插帧样片看效果（用户 11-19）。若插帧：插值作用在输入帧上，四种方法都基于插值后的输入生成，12 s 实际时长 = 120 个原生帧 → 300 帧；若原生：120 帧 10 fps。两种方式下 DS、MP 都需要按新的 120 帧窗口重跑或截取 |
| D12 | HELVIPAD 的 DeepSynthesis 改用正确的深度尺度 raw/256 重新标定并重跑（用户 11-20）；MotionParallax 输入的 uint16 回绕影响极小，不重跑、沿用现有输入（用户 11-20） |
| D13 | 第二部分 PAw 的修正半径按被试选择（用户 11-20，方案 A）：注视练习前的自然注视范围测量（同日改为连续向外扫动：圆点从中央沿 8 个方向各 2 次匀速向外移动，a = 头部开始转动时圆点偏心度的中位数）得到被试的 a，程序从 θmax ∈ {10, 15, 20, 25, 30}° 中取最接近 a 的一个（超出范围取端点，如 a = 40° → 30°；步骤关闭或跳过时 15°，论文默认值），衰减带 5° 不变。因此第二部分每个片段（near_A–F 与练习 train_near，共 7 段）都要渲染 1 个 PA 与 5 个 PAw 变体，文件名 `<clip_id>__pa.mp4` 与 `<clip_id>__paw_t<θ>.mp4`（如 `near_A__paw_t20.mp4`）；程序与清单见 `docs/walkthrough_issues_20261119.md` 第 52–58 条 |

## 1. 统一规格

- 每眼 5768×2884，上下排列（左眼在上）→ 5768×5768；25 fps；300 帧（12 s）。
- 编码：`hevc_nvenc`，`-preset p5 -tune hq -rc vbr -cq 20 -b:v 0 -maxrate 200M -bufsize 400M -g 25 -profile:v main -tag:v hvc1`，yuv420p，与解码测试片相同。
- 放大：`v360=input=e:output=e:interp=lanczos`，分眼处理后再上下拼接（避免 `in_stereo=tb` 时 `w`/`h` 含义不明，盘点第 5 节未核实项），每种方法、每个片段同一命令。
- 眼序：每种方法生成后抽 1 帧做左右眼核对（Ours：`9178990` 之后 left 为物理左眼；DS：X2 报告为物理顺序；MP：`validation/b_eyeorder_sectors.json`）。
- 每帧对应深度：Ours 离线入口 `--depth-update-interval 1 --depth-update-lag-intervals 0`；DS 逐帧合成；MP 官方逐帧分层（层视频 301 帧）。
- 深度标定沿用论文表 III 的设置：Replica 序列级 GT 标定（0.1–10 m），HELVIPAD、PAIR360 用 C0（0.1–100 m）。

## 2. 片段分配（建议，待确认）

第一部分需要四种方法，只能用已有或可补跑 DS/MP 的序列；第二部分与练习只需 Ours 或 Mono，可以用新生成的 Replica 序列。

| 用途 | 片段 | 来源序列 | DS/MP 现状 |
|---|---|---|---|
| 第一部分 虚拟室内 | vin_A / vin_B / vin_C | Replica apartment_0 / office_1 / room_0 | 已有（2184×1092） |
| 第一部分 dummy | dummy_indoor | Replica office_4 | 已有 |
| 第一部分 真实室内 | rin_A / rin_B / rin_C | HELVIPAD val REC_12_IN / test 20240120_REC_06_IN / test 20240127_REC_01_IN | REC_12_IN 已有；另两条需补跑 DS、MP |
| 第一部分 真实室外 | rout_A / rout_B / rout_C | PAIR360 CollegeofEngineering_Seq0 / ParkingLotPeaceAmphitheater_Seq1 / 1stDormitory_Seq1 | 已有；**1stDormitory_Seq1 只有 250 帧（10 s）**，见待确认 Q2 |
| 第一部分练习 | train_indoor（ours）、train_outdoor（mono） | Replica 新生成 1 条；PAIR360 CollegeofPhysicalEducation_Seq0 | 不需要 |
| 第二部分 近景 | near_A–D | Replica hotel_0 / office_0 / room_1 / room_2（盘点中 2026-04 近处注视候选所在序列） | 不需要 |
| 第二部分 近景 | near_E / near_F、train_near | Replica 新生成 3 条 | 不需要 |

需新生成的 Replica 序列：4 条，场景取与已有 8 条不同的 `apartment_1`、`frl_apartment_0`、`office_2`、`office_3`（场景网格在 173 上齐全，共 18 个场景），4K、300 帧、25 fps，带深度；第二部分用的 3 条需要在参考方向附近有近处物体，生成后先出预览帧给用户挑。

## 3. 生成任务

| 编号 | 任务 | 地点 | 估计 |
|---|---|---|---|
| S1 | Replica_gen 新生成 4 条（habitat 环境，沿用 run_0007/0009 的生成参数，4K 直接渲染） | 173 GPU 0–3 | 待首条实测 |
| S2 | Mono：源帧 → 4K（PAIR360 用 INTER_AREA 缩到 5768×2884，与 4KDOWN 一致；HELVIPAD 用 `v360` 放大）→ 上下复制 → 编码 | 173 | 每段几分钟 |
| S3 | Ours PA：第一部分 10 段 + train_indoor；Ours 第二部分 7 段 × （PA + PAw θmax 10、15、20、25、30°，衰减带 5°）= 42 段（D13）；离线入口，代码 `e58279d`（worktree `/mnt/nvme1/Pano2stereo_wt_prealloc`），DA²；HELVIPAD 在原生 1932×966 渲染后放大 | 173 GPU 0–3 | 共 11 + 42 = 53 次渲染（D13 之前为 25 次），每次待实测；PAw 的 θmax 参数名与取值待在离线入口中核实 |
| S4 | DS 补跑：HELVIPAD 2 条（`deepomnistereo_bl`，先拟合深度标定） | 173 | 每条待实测 |
| S5 | MP 补跑：173 打包输入 → **本机 MATLAB R2026b** 跑官方预处理 → 回传 173 渲染 ERP（`mp_ods_render.py`，1932×966） | 本机 + 173 | REC_12_IN 实测：主阶段 2 h 42 min + 其余 5 min，每条约 3 h，占满本机 8 核 |
| S6 | DS、MP（全部第一部分片段）：已有或补跑的输出 → `v360` 放大到 5768×2884 → 上下拼接 → 编码 | 173 | 每段几分钟 |
| S7 | 校验：分辨率、帧数、眼序抽查、每段首帧/中间帧/末帧拼图给用户看；生成刺激清单（文件名、来源、命令、参数、sha256） | 173 + 本机 | |
| S8 | 传回本机 `Stimuli/`（正式刺激约 60–70 MB/段；D13 后共约 73 段，约 5 GB） | | |

顺序：S2、S6（已有输出部分）、S3 可以马上排进队列（与 E–H 共用 GPU 0–3，离线渲染不计时，不影响 E–H 结果）；S1 生成后再做第二部分剩余的 S3；S4、S5 与之并行；S5 的本机 MATLAB 安排在不做 VR 测试的时段（如夜间）。

磁盘：Ours 4K 每次渲染只保留修补后的两眼 PNG（约 12 GB/次，按 5768×2884 PNG 约 20 MB 估计），D13 后 53 次约 640 GB，超过 `/mnt/disk4` 的剩余 626 GB。用户 11-20：中间 PNG 可分散放在 173 的 disk1–4 与 nvme1 上；所有中间产物在刺激核对完成后的最后阶段统一清理，删除前列出确切路径请用户批准；中间 PNG 在刺激核对完成前保留，删除前另行请用户确认。

## 4. 待确认

- Q1 第 2 节的片段分配。
- Q2 PAIR360 1stDormitory_Seq1 只有 250 帧（10 s）：接受 10 s，或换一条 ≥300 帧的序列（2ndDormitory_Seq0 / CollegeofLifeScience_Seq0 / CollegeofPhysicalEducation_Seq0，需补跑 DS、MP）。
- Q3 HELVIPAD 的帧间隔未核实：相机 30 fps、LiDAR 10 fps，数据集帧是否按 LiDAR 10 Hz 采样不清楚。若为 10 Hz，300 帧按 25 fps 播放会快约 2.5 倍。需先核实（官方附录 A 或相邻帧运动量），再决定是否处理。
- Q4 本机 MATLAB 补跑 MP 的时段（每条约 3 h，占满 8 核，与 VR 测试冲突）。
- Q5 新生成的 4 个 Replica 场景（建议 apartment_1、frl_apartment_0、office_2、office_3）。

## 5. 未核实

- `hevc_nvenc` 5768×5768 编码：解码测试片已成功编码（Level 6.0，见第 6 节）。
- Windows 与 Pico 串流下 5768×5768 HEVC 的解码与丢帧：待用户用测试片实测；本机已装 `Microsoft.HEVCVideoExtension 2.5.48.0`。
- Ours 4K 离线渲染的单次耗时与 PNG 实际大小。

## 6. 解码测试片（D7，已完成）与去转向对比片（D8）

- 173：`/mnt/disk4/Pano2stereo_r1/E5_stimuli/decode_test/decodetest_replica_office0_mono_tb5768_hevc.mp4`（生成脚本 `make.sh`、日志 `make.log` 同目录）。
- 来源：Replica office_0 原生 4K（run_0009）`rgb` 300 帧，两眼同图（mono），上下排列。
- ffprobe：`hevc Main, hvc1, 5768×5768, yuv420p, level 180 (6.0), 25 fps, 300 帧, 44.3 Mb/s`，63 MB；sha256 `75158f5e88af05f6bf6cf6d37ed7b422c2f9c921010718a0725e9251fc9ceb56`（本机副本一致）。
- 本机：`Stimuli/_decode_test/`；并复制为 `Stimuli/train_indoor__ours.mp4`（第一部分第一段练习），原测试片备份为 `Stimuli/_decode_test/orig_testclip_train_indoor__ours.mp4`。
- 去转向对比片：`decode_test/decodetest_replica_office0_mono_tb5768_hevc_yawstab.mp4`（同一片段、同一编码参数，按轨迹真值去掉相机转向；生成脚本 `make_stab.py`，平移量写在同名 `.json`），sha256 `e6273b72f610c72a25d37a65b483ae775d28ebd18d5702e18efb22db713c0a88`；本机复制为 `Stimuli/train_outdoor__mono.mp4`（第一部分第二段练习），原测试片备份为 `Stimuli/_decode_test/orig_testclip_train_outdoor__mono.mp4`。
- 方向验证：StdTest `office_0_seed_300` 与 run_0007 `seq_000` 前 300 帧逐帧相同（md5）；图像相关估计的累计转角：原片 179°，sign = −1 后 34°（剩余主要来自平移造成的估计误差），sign = +1 为 325°。验证用的 1080 平移帧在 173 `/mnt/disk4/Pano2stereo_r1/E5_stimuli/yawtest/`（`s-1`、`s1` 各 300 张 PNG），删除前请用户确认。

## 7. 帧率样片（D11）

- 173：`/mnt/disk4/Pano2stereo_r1/E5_stimuli/interp_test/`（脚本 `make.sh`、日志 `make.log`）。来源 HELVIPAD val 20240124_REC_12_IN 前 120 帧（原生 10 Hz 下 12 s），mono，`v360` lanczos 放大到每眼 5768×2884，上下排列 HEVC（同解码测试片参数）。
- B：原生 10 fps，120 帧，12.0 s，43 Mb/s，sha256 `33d22330d6bd102087917295dfacce1abd67b4e4642a878c480f0bef7d44a952`。
- C：FFmpeg `minterpolate`（`mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1:scd=none`）插到 25 fps，插值前左右各补 192 px 循环边界、插值后裁回，296 帧，11.84 s，73 Mb/s，sha256 `840476ee8ab30e7a8cfd165c7a11456ccb5c35a0b04b16b664fa93f3f39836ca`。
- 本机：`Stimuli/_decode_test/`；C 复制为 `Stimuli/dummy_indoor__ours.mp4`，B 复制为 `Stimuli/dummy_indoor__mono.mp4`（P01 第一部分第 1 组的第 1 段与第 4 段；中间两段为 DS、MP 占位），原测试片备份在 `_decode_test/orig_testclip_dummy_indoor__*.mp4`。

## 8. 真实片段窗口（用户 11-19：按转向最少的 12 s；停车段与有运动两种都准备，11-20 看后再定）

转向用 `tools/stimuli/yaw_windows.py`（相位相关估计偏航，1 s 平滑，窗口内累计转角）；“有运动”用 `yaw_windows_moving.py`（1 s 画面变化中位数 ≥ 序列中位数的 70%）。结果：173 `/mnt/disk4/Pano2stereo_r1/E5_stimuli/windows/`。原生 10 Hz，每窗 120 帧；起始帧为排序后文件序号（0 起）。

| 片段 | 序列 | 窗口（转向最少） | 有运动窗口 |
| --- | --- | --- | --- |
| rin（REC_12_IN） | HELVIPAD val 20240124_REC_12_IN | 250（25°，运动正常） | 同左 |
| rin（REC_06_IN） | HELVIPAD test 20240120_REC_06_IN | 300（15°） | 同左 |
| rin（REC_01_IN） | HELVIPAD test 20240127_REC_01_IN | 510（17°） | 同左 |
| rout（1stDorm） | PAIR360 1stDormitory_Seq1 | 120（15°） | 同左 |
| rout（CoE） | PAIR360 CollegeofEngineering_Seq0 | 1550（3°，画面变化 27%，停车段） | 1790（15°，82%） |
| rout（Parking） | PAIR360 ParkingLotPeaceAmphitheater_Seq1 | 1110（15°，60%） | 3440（17°，78%） |
| train_outdoor（PhysEd，只需 mono） | PAIR360 CollegeofPhysicalEducation_Seq0 | 70（0.5°，16%） | 280（35°，70%） |

对比片（mono，原生 10 fps，120 帧，7680→5768 area 缩放，上下排列 HEVC，90–118 Mb/s）：173 `E5_stimuli/window_test/`（`SHA256SUMS`）；本机放在第一部分第 4 组 rout_B（P01 播放顺序）：第 1 段 `rout_B__motionparallax` = CoE 停车段，第 2 段 `rout_B__mono` = CoE 有运动，第 3 段 `rout_B__ours` = Parking 偏慢段，第 4 段 `rout_B__deepsynthesis` = Parking 有运动；原测试片备份在 `_decode_test/orig_testclip_rout_B__*.mp4`。

## 9. 正式刺激在本机的存放（用户 11-20：全部准备好后下载到 _study 目录，按类型存好）

结构：`Stimuli/part1/{virtual_indoor,real_indoor,real_outdoor,dummy,practice}/`、`Stimuli/part2/{near,practice}/`，文件名不变（`<clip_id>__<condition>.mp4`，第二部分 PAw 为 `<clip_id>__paw_t<θ>.mp4`），根目录放 `manifest.json`（每个文件的来源序列、窗口、方法、版本、生成命令、分辨率、帧数、sha256）；测试片与样片移到 `Stimuli/_samples/`。

已实现（用户 11-19/11-20）：配置 `stimulus_folders` 与 `file_pattern = "{folder}/{clip_id}__{condition}.mp4"`，清单的 `file`、`file_a`、`file_b` 带子文件夹（正斜杠）；P01–P40、T01–T05（`StudyTools/plans_pilot`）、AP01 已重新生成，P01–P30 与 AP01 除文件名的子文件夹外与上一版完全相同。程序在 `stimuli_dir` 下解析这些相对名。本机 `Stimuli/` 根目录现有的测试片与样片（如 `train_indoor__ours.mp4`、`dummy_indoor__*.mp4`、`rout_B__*.mp4`）需要由用户移到对应子文件夹（或 `_samples/`），否则 QA 模式会播放占位图案。

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

## 10. 备用数据集（用户 11-20 记录，暂不使用）

如果 Replica 片段不够用，可以改用 Blender rich_scenes_v3（用户 11-20：“实在不行启用Blender数据集”）：
- 位置：173 `/mnt/nvme1/PanoDepthDynamicLUT/data/virtual_scenes/blender/rich_scenes_v3/`。
- 场景索引：`scene_index.json`，共 8 个场景，包括 apartment、studio、bedroom、cafe、gallery 等。
- 每个 capture 的帧：`capture_*/shared/<sequence>/frame_NNNN/target/rgb.png` 与 `depth_mm.png`。
- 用法：见各 `capture_*/USAGE.md`。

启用前需先核实以下各项：分辨率与帧数（4K、300 帧、25 fps）、深度编码（光线距离或平面深度）、相机运动与朝向（是否需要去转向），以及 DA² 序列级标定能否按论文做法拟合。上面所列的核实项均未做。

## 11. 第二部分片段与参考方向（用户 11-20 定稿）

参考方向为内容水平角（右为正，0 = 片段正前方），俯仰均为 0；已写入两份配置的 `part2.clips[].reference_yaw_deg` 与练习片段，清单 P01–P40、T01–T05、AP01 已重新生成（只有参考方向变化）。

| 片段 | 来源（Replica 序列与帧） | 参考方向 |
| --- | --- | --- |
| near_A | hotel_0 seq_000 | +60° |
| near_B | hotel_0 seq_008，帧 200–499 | −120° |
| near_C | office_0 seq_002，帧 75–374 | −60° |
| near_D | office_0 seq_011，帧 100–399 | +30° |
| near_E | hotel_0 seq_007，帧 0–299 | −30° |
| near_F | apartment_0 seq_007，帧 0–299 | −30° |
| train_near（第二部分练习） | apartment_0 seq_003，帧 175–474 | −90° |
| train_indoor（第一部分练习，只需 Ours PA） | room_0 seq_003，帧 25–324 | — |

## 12. 预实验版本的片段（用户 11-22 定稿，方案一）

用户 11-22 决定：虚拟场景改为 Replica 与 Blender rich_scenes_v3 混用，每个场景在全部实验中只出现一次；贴墙的 vin_B（Replica office_1）与 dummy_indoor（Replica office_4）不再使用；第二部分保持每段 3 个试次（PA→PAw、PAw→PA、相同各 1 个）。配置版本 `v4-draft-2026-11-22`，P01–P40、T01–T05 已重新生成；AP01 只用于桌面自动检查，未改。Blender 片段的筛选与生成见 `docs/previews_blender_20261122/`。

| 位置 | 片段 | 来源与场景 | 参考方向 |
| --- | --- | --- | --- |
| 第一部分练习 | train_indoor_room2（Ours） | Replica room_2，即备用片段 near_D_room2_ref30 的 PA | — |
| 第一部分练习 | train_outdoor（Mono） | 不变 | — |
| 第一部分 dummy | bin_C | Blender workshop traverse | — |
| 第一部分正式（虚拟） | vin_A、vin_C | Replica apartment_0、room_0 | — |
| 第一部分正式（虚拟） | bin_A、bout_A、bout_B | Blender cafe、garden、plaza（traverse） | — |
| 第一部分正式（实拍） | rin_A–C、rout_A–C | 不变 | — |
| 第二部分练习 | train_near_room1 | Replica room_1，即备用片段 near_C_room1_refm60 | −60° |
| 第二部分正式 | bnear_A、bnear_B、bnear_D、bnear_F | Blender apartment curved_yaw、gallery near_lateral、studio traverse、bedroom curved_yaw | +55°、+30°、−60°、−10° |
| 第二部分正式 | near_B、near_C | Replica hotel_0 seq_008、office_0 seq_002 | −120°、−60° |

- 第一部分共 50 段：练习 2 段、dummy 1 组、正式 11 组（每组 4 种方法）。第二部分共 20 个试次：练习 2 个、正式 18 个。
- 两段备用片段改用新的片段名，视频字节不变，sidecar 中记有 `renamed_from`。
- 第二部分的 Replica 两段放在配置列表的最后，每位被试的两种相同配对在 Blender 与 Replica 内各自平衡。
- 未使用但保留在 `Stimuli/` 中的片段：bin_B、bnear_C、bnear_E、btrain_near，以及 Replica 的 vin_B、dummy_indoor、train_indoor、near_A、near_D–F、train_near。
- 预实验用的刺激只含清单引用的 92 个文件，放在本机 `D:\WorkSpace\E5_pilot_20261123\Stimuli\`（`SHA256SUMS`、`files.txt` 在其上一级）。

### 12.1 实验版本（用户 11-23 决定：实验按此版本开始）

- 程序、配置与清单：提交 `bb1755e`（分支 `feature/subjective-study`）。配置 `StudyTools/study_config.example.json`，`config_version` = `v4-draft-2026-11-22`，sha256 `7b9f621554aa9462feab8198db3b7d4d7013b4357b6280b433396734c77b1487`（与每份清单的 `config_sha256` 相同），清单格式 5。
- 被试编号：预实验 T01–T05（`StudyTools/plans_pilot`），正式 P01–P40（`StudyTools/plans`）。AP01 只用于桌面自动检查。
- 刺激：92 个文件，`D:\WorkSpace\E5_pilot_20261123\Stimuli\`，复制到实验电脑的 `<项目根目录>\Stimuli\`。清单 `SHA256SUMS` 的 sha256 为 `c9e423fb70ff6a4b719d1d5cf5082f6928b1f51be6172c3b30a3563ded604c27`。
- 全部立体内容按 IPD 0.063 m 生成（Ours、DeepSynthesis V1V2、MotionParallax 的运行记录均已核对）；Mono 两眼同图。
- 之后若更换片段、配置或清单，须另起版本号并在此记录。

### 12.2 冻结后的程序改动：续做与重新开始（用户 11-23 决定）

- 原因：正式实验中 P02 在第一部分第 12 段程序出错无法点击，退出后只能改用 QA 模式快进续做。用户要求加入续做功能。
- 行为：正式模式载入的编号已有正式数据时，签到页询问“继续”或“重新开始”。继续：在原文件夹从第一个没有结束记录的步骤开头重做，该步原有的不完整数据移到 `superseded_<UTC>/`；需要头显的步骤先做头显佩戴与重定正前方；已测的修正半径沿用。重新开始：原正式文件夹只改名移到备份文件夹的 `replaced/`（或数据文件夹下的 `_replaced/`），不复制、不删除，两处文件夹都写明原因与去向。QA 模式不变。细节见 `StudyTools/README.md` 数据契约一节。
- 不改变试次清单、刺激与试次顺序；配置版本仍为 `v4-draft-2026-11-22`。分析程序只读会话文件夹顶层的文件，`superseded_*` 与 `replaced` 中的数据不进入分析。
- 验证：离线编译与 67 项自动测试通过（其中续做相关 13 项）；Unity 中的实际流程尚未测试，实验电脑拉取并重新打包后，建议先用测试数据文件夹走一遍再用于被试。
- 提交：本节所在的提交（见 git log）。

### 12.3 冻结后的程序修复：主试确认框之后鼠标失效（用户 11-24 报告）

- 现象（用户 11-24）：在头显佩戴步骤以外按 R（或 Shift+R）设置正前方并按 Y 确认后，电脑端鼠标不能再点击，被试填问卷和主试操作都受影响；头显手柄照常可用。
- 原因：主试确认框（紧急重定正前方 R、F8 跳过）打开时，`SuspendInput` 关闭 Unity 的 `EventSystem` 以屏蔽其他输入；关闭确认框后恢复时却按 `EventSystem.current` 查找，而关闭后的 EventSystem 已从 `current` 所依据的列表中移除，查到的是空值，于是一直没有重新启用。头显手柄自行做射线检测，不经过 EventSystem，所以不受影响。程序内重新开始也不恢复，只能退出程序。
- 11-23 的旁证：P02 正式会话的 `events.csv` 在立体视测试中有 5 次 `recenter_emergency`（13:59–14:00），之后第一部分用手柄完成，到第一个需要鼠标的电脑端页面（SSQ `S_mid1`，14:21）卡住，与当天“程序出错无法点击”的报告一致。其余 7 个会话没有这类确认框记录。
- 修复（`StudyController.cs`）：关闭时记住被关闭的 EventSystem 对象，恢复时直接重新启用它；程序内重新开始（清理状态）时也做同样的恢复。只改输入的暂停与恢复，不改变试次清单、刺激、试次顺序与数据文件；配置版本仍为 `v4-draft-2026-11-22`。
- 验证：离线编译与 67 项自动测试通过；EventSystem 的启停只能在 Unity 运行时验证，实验电脑重新打包后请在一个电脑端页面（如 SSQ）按 R、Y 后再用鼠标点击一次确认。
- 提交：本节所在的提交（见 git log）。

### 12.4 冻结后的程序改动：对准提示箭头（用户 11-24 决定）

- 原因（用户 11-24）：第二部分正式实验中，有被试头偏了，对准一直不开始，但被试不知道要纠正，一直在等。用户要求在所有对准黄色十字的地方加提示，不能只给文字，要给箭头：头部偏下时在十字下方给向上的箭头，偏右下时在十字右下给向左上的箭头。
- 行为：头部连续 2 s 不在对准范围内（`alignment.tolerance_deg`，5°）时，十字旁出现黄色箭头指向十字，放在十字靠被试视线的一侧、离十字中心 6°；偏离很大时箭头离视野中心不超过 20°；方向按水平与俯仰的实际偏离连续计算；进入范围即消失。有偏离文字的步骤加“请按箭头方向转头”。适用于第二部分每个试次开始前与转头中断后的重新对准、注视练习开始前、自然注视范围每次扫动开始前（都用同一个 `AlignToReference`）。每次出现写入 `events.csv` 的 `align_hint`。详见 `StudyTools/README.md`。
- 影响范围：箭头只在对准阶段出现，播放、评分与头部测量开始后不出现；对准容差、停留时间、试次清单、刺激、试次顺序与数据文件的列都不变；配置版本仍为 `v4-draft-2026-11-22`。使用新版之前与之后的会话只在对准阶段的提示上不同，`part2.csv` 的 `align_s`（对准用时，只作描述）可能变短；从哪位被试开始使用新版，请在实验记录表中注明。
- 实现：新文件 `Assets/Scripts/Study/StudyAlignHint.cs`（位置与方向的计算、箭头的几何），`StudyController` 的对准流程接入；不改 Fable 维护的界面文件。
- 验证：离线编译与 74 项自动测试通过（新增 7 项：头部偏下、偏右下、偏离过大、偏离略超容差、来回移动的范围、方向重合、屏幕方向角）。箭头在头显中的实际显示只能在 Unity 运行时确认，重新打包后请在第二部分练习试次中故意把头偏开 2 s 以上看一次。
- 提交：本节所在的提交（见 git log）。
