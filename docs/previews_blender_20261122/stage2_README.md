# Stage 2：Blender 刺激的生成（2026-11-22）

依据：用户 11-22 约 12:00 的决定（经主会话转达）“现在开渲，128 采样”“用建议的片段”。后续被分类器拦下的下游步骤由主会话启动。

## 片段编号与来源

都是 4K 5768×2884，25 fps 时间轴（t = t0 + k/25，共 300 帧），固定朝向 H（窗口均值，`yaw_rotation` 约定）。content yaw 0 为 H，向右为正。

| 编号 | 部分 | scene / seq | t0 s | H ° | 参考 ° | 渲染 label |
|---|---|---|---|---|---|---|
| bnear_A | 第二部分 | apartment / curved_yaw | 2.9 | +12.34 | +55 | apartment_curved_yaw_t029 |
| bnear_B | 第二部分 | gallery / near_lateral | 2.9 | +13.56 | +30 | gallery_near_lateral_t029 |
| bnear_C | 第二部分 | cafe / near_lateral | 2.9 | +11.15 | +85 | cafe_near_lateral_t029 |
| bnear_D | 第二部分 | studio / traverse | 2.9 | +1.15 | −60 | studio_traverse_t029 |
| bnear_E | 第二部分 | bedroom / near_lateral | 0.0 | −11.92 | −70 | bedroom_near_lateral_t000 |
| bnear_F | 第二部分 | bedroom / curved_yaw | 2.9 | +106.08 | −10 | bedroom_curved_yaw_t029 |
| btrain_near | 第二部分练习 | studio / near_lateral | 2.9 | +8.83 | −30 | studio_near_lateral_t029 |
| bin_A | 第一部分室内 | cafe / traverse | 1.5 | +10.0 | — | cafe_traverse_t015 |
| bin_B | 第一部分室内 | apartment / traverse | 1.5 | +12.5 | — | apartment_traverse_t015 |
| bout_A | 第一部分室外 | garden / traverse | 1.5 | +15.0 | — | garden_traverse_t015 |
| bout_B | 第一部分室外 | plaza / traverse | 1.5 | +7.5 | — | plaza_traverse_t015 |

## 流程（173）

1. **渲染**：`/mnt/disk4/Pano2stereo_r1/E5_blender/tools/e5b_batch.py tools/jobs_go.tsv --no-auto-tile`，8 卡，每 50 帧一块，128 spp。输出在 `renders/<label>/{rgb,depth}`，日志在 `logs/stage2/batch.log`。
2. **下游**：`tools/e5b_pipeline.py`。每段渲染完成后：
   1. DA² 标定：
      - 由 4K 渲染降采样生成 2184×1092 拷贝（rgb 用 INTER_AREA，深度用最近邻），放在 `calib/StdTest/<replica_gen|pair360>/blender_<label>`。
      - DA² raw 1080：`src/test_offline_video.py --npyonly --downsample 1080 --postprocess-mode raw`。
      - 拟合：`tools/e5b_fit_calib.py`，即原样调用 `fit_stdtest_depth_calibration.py`，只加了 blender 数据类型。参数为 pixel-stride 16、log_depth_power；深度范围室内 0.1–10 m，室外 0.1–100 m。
   2. Ours：
      - 设置同 `run_part2_final.sh`（表 III Ours DA²，`--downsample 4k --fps 25`），depthrange 室内 [0.1,10]、室外 [0.1,100]。
      - 第二部分：PA，以及 PAw θ 10/15/20/25/30（falloff 5，`--gaze-forward-unit=<u>`，u = (cos ref, sin ref, 0)）。
      - 第一部分：PA。
   3. 编码：`make_part1_real.py`（E5 常数，5768 原样，不平移）。第一部分另编 mono。
   4. `tools/e5b_finalize.py`：生成 `final_blender/manifest.json`、`SHA256SUMS`、`STATUS.txt`，并核验 ffprobe 与 sha256。
3. **DS V1V2、MotionParallax**：各有独立的启动器（另见各自的说明），输出到同一个 `final_blender/Stimuli/part1/...`。
4. **下载**：本机 `.scratch/e5_blender/tools/e5b_download.sh`，下载到 `D:\WorkSpace\Pano2StereoVR_study\Stimuli_blender\` 并核对 sha256。

**StdTest 路径标签**：仓库离线入口只在能识别出 StdTest 布局的路径上才允许 raw 输出和标定（`src/common/standard_dataset.py`，会解析符号链接）。因此：
- 1080 拷贝和 4K 输入（同盘硬链接，`ours_in/StdTest/...`）都放在 `StdTest/<replica_gen|pair360>/blender_<label>` 下。
- 序列键为 `replica_gen__blender_<label>`（室内）或 `pair360__blender_<label>`（室外）。

这个标签只用于通过入口检查；标定按 Blender GT 和上述深度范围拟合。

## 结果

（完成后补）
