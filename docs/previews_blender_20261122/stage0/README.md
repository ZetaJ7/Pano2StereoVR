# Stage 0：4K 重渲计时与 2K 备选（2026-11-22）

本文只报数，是否采用由用户决定。

## 渲染脚本（173）

目录：`/mnt/disk4/Pano2stereo_r1/E5_blender/tools/`

- 主程序为 `e5b_render.py`，另有 `e5b_depth_writer.py`（EXR→uint16 PNG）、`e5b_traj.py`（轨迹）和 `srcpkg/pano_dynamic_lut/`。
- `srcpkg` 里的轨迹代码从 `capture_garden_v1/provenance` 原样复制。24 条序列与 `trajectories/*.jsonl` 逐帧比对，误差为 0。
- 原 .blend 只读打开，不保存。
- 渲染设置与数据集 `cycles_oidn_gpu_tiled_v1` 相同：OptiX、OIDN GPU、128 spp、adaptive 0.01、seed 606。只出中心 ERP 的 RGB 和 Cycles Z（射线距离），不分块（`--no-auto-tile`）。
- 深度：uint16 PNG，室内 0.001 m、室外 0.002 m，0 表示无效（天空）。
- 时间轴：t = t0 + k/25，300 帧，取 30 fps 路径上的小数帧位置。室外货车动画用子帧（frame_set subframe）驱动，已核对：101.48 帧的位置正好是 101 与 102 帧的线性插值。
- 朝向：固定 yaw，用 `--heading`，与 `yaw_rotation` 同一约定。
- 输出朝向为 Blender 原生 ERP，即数据集 target 的水平翻转：图像右侧对应相机右侧。heading +30° 时内容左移 30.0°。

## 核对

- **2048 重渲 vs target（翻转后）**：RGB PSNR 65 dB（p99 差 1/255），深度逐像素相同。
- **4K 降采样到 2048 vs target**：
  - bedroom：PSNR 40.7 dB，深度相对误差中位 0.1%，99.3% 像素误差在 1% 以内。
  - garden：31.6 dB，91% 在 1% 以内，差别在植被和天空边缘。
- **参考方向**：用固定朝向在 1024 宽下渲染 P2-1、P2-3、P2-4，近物比例出现在 +ref，镜像方向 −ref 为 0，与 stage 1 的时间序列一致：

| 片段 | +ref 实测，0/3/6/9/12 s | stage 1 |
|---|---|---|
| apartment curved_yaw @+55 | 0 / .050 / .122 / .118 / .052 | 0 / .036 / .122 / .150 / .094 |
| cafe near_lateral @+85 | 0 / 0 / 0 / .325 / .483 | 0 / 0 / 0 / .301 / .489 |
| studio traverse @−60 | 0 / .138 / 0 / .094 / .127 | 0 / .129 / 0 / .060 / .127 |

## 计时（RTX 4090，5768×2884，128 spp，不分块）

每帧另加约 1.5 s 写 PNG；每块（50 帧）开头多 25–35 s 加载。

| 场景 | s/帧 |
|---|---|
| apartment | 32.8 |
| bedroom | 27.6 |
| cafe | 32.2 |
| gallery | 39.4 |
| studio | 32.4 |
| workshop | 24.0 |
| garden | 11.5–19.4（约 13.6） |
| plaza | 13.5–20.3 |

- 数据集原配置用 2048 分块，更慢：bedroom 35–41 s，garden 18–24 s。不分块的输出与分块相同（PSNR 65 dB，深度逐像素相同）。
- 64 spp：bedroom 14 s，garden 7 s。与 128 spp 相比 PSNR 室内 45.4 dB、室外 38.7 dB。
- 2884×1442 半分辨率（分块）：bedroom 7.0 s，garden 3.9 s。降到 2048 与 target 比，bedroom 42.9 dB，garden 36.6 dB。

8 卡墙钟估计（128 spp，按 stage 1 建议片段，每 50 帧一块分到各卡）：

| 范围 | 墙钟 | 64 spp 墙钟 |
|---|---|---|
| 第二部分 6+1 | ≈2.5 h | ≈1.3 h |
| 再加第一部分 4 段 | 累计 ≈3.5 h | ≈1.8 h |
| 第一部分扩到 8 段 | 累计 ≈4.8 h | ≈2.5 h |

- RGB PNG 约 48 MB/帧，每段约 15 GB。

## Stage 2 启动器

`tools/e5b_batch.py JOBS.tsv --no-auto-tile`

- jobs 草稿：`tools/jobs_draft.tsv`，按 stage 1 建议生成，未启动。
- 输出：`/mnt/disk4/Pano2stereo_r1/E5_blender/renders/<label>/{rgb,depth}`。
- 每块开始前检查 GPU 显存是否空闲；失败自动重试一次；可逐帧续跑。

## 2K 备选（只作后备）

| 方法 | s/帧 | bedroom PSNR 全幅 / 裁剪 | bedroom 时间差误差 / 比 | garden PSNR 全幅 / 裁剪 | garden 时间差误差 / 比 |
|---|---|---|---|---|---|
| (a) 2K + v360 lanczos | 1.7（CPU） | 35.7 / 26.8 | 1.75 / 0.78 | 26.3 / 18.5 | 5.13 / 0.77 |
| (b) 输入 SR x4plus | 7.7（4090） | 35.0 / 26.9 | 2.05 / 0.90 | 26.1 / 18.1 | 5.38 / 0.86 |
| (b) 输入 SR animev3 | 1–17，不稳定，均值约 7.5 | 33.8 / 26.4 | 1.74 / 0.78 | 26.0 / 18.4 | 5.06 / 0.82 |

- 时间差误差 = 10 帧中 mean|ΔI_M − ΔI_4K|（灰度 0–255），比 = mean|ΔI_M| / mean|ΔI_4K|。4K 的 mean|ΔI| 为 bedroom 2.44、garden 8.23。
- 3 种方法的时间差误差相近。比都小于 1，说明帧间变化比 4K 少（细节被平滑）。10 帧内没看到 SR 引入明显闪烁。
- 裁图：`crop_bedroom_f105.png`、`crop_garden_f105.png`，都是 5768 原生分辨率、最近邻放大 2 倍。
  - 4K 明显最清晰。lanczos 发糊。
  - x4plus 边缘锐，但植被呈油画状纹理。
  - animev3 的树叶卡通化。
- SR 的 wrap pad 为 64 px（2048 输入），之后用 INTER_AREA 从 8192 降到 5768。指标见 `eval_*.json`。
