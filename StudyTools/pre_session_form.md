# 线上预填表单（草稿，已停用）

> **已停用（2026-11-19）**：用户决定知情同意、基本信息与视力自报改为到场纸质填写，不再使用线上表单。本文件只作为信息页与同意书条目的参考保留，未删除。签到页的勾选项改为“纸质知情同意书与基本信息表已签署”（`checkin.json` 字段 `paper_forms_confirmed`）；基本信息表录入后仍按第 4 节的列名存为 `pre_session.csv`，供分析程序读取。


用途：被试到场前完成信息阅读、知情同意、基本信息和视力自报，现场只做口头提醒。表单平台由用户选择（如问卷星、腾讯问卷），导出 CSV 后改名为 `pre_session.csv`，列名按第 4 节。

依据：ITU-T P.919 §11.1 与附录 III（信息页内容清单、同意书样例；附录 III 写明签同意书之前必须先提供信息页）、附录 V 背景问卷样例（标注"测试前由被试填写"）。附录 III 的同意书样例含主试见证签字一项；线上提前签署是否可行，以 IRB 批准为准。

## 1. 信息页 / Information sheet（按 P.919 附录 III 的 16 项）

| # | 项目 | 内容（草稿；方括号内待填） |
| --- | --- | --- |
| 1 | 研究名称 / Title | 全景视频立体化的主观质量评价 / Subjective evaluation of stereo conversion for 360° video |
| 2 | 负责人 / Principal researcher | [姓名、邮箱、职务] |
| 3 | 单位 / Institution | [单位] |
| 4 | 流程与时长 / Session structure and duration | 到场约 35 分钟：戴头显前填一份简短症状问卷；头显内立体视小测试；第一段观看约 11 分钟；摘下头显休息约 2 分钟并填症状问卷；第二段观看约 10 分钟；最后填症状问卷和两三道问题。/ About 35 min on site: a short symptom questionnaire, a brief stereo-vision check in the headset, about 11 min of viewing, a 2-min break with the questionnaire, about 10 min of viewing, and a final questionnaire. |
| 5 | 地点 / Location | [实验室地址] |
| 6 | 文件编号 / Document ID | [日期、版本号] |
| 7 | 研究目的与数据处理 / Purpose and data processing | 比较不同立体化方式下 360° 视频的观看体验；评分、问卷和头部转动数据只用于本研究的统计分析。/ To compare the viewing experience of 360° videos under different stereo conversion methods. Ratings, questionnaires and head-rotation data are used only for statistical analysis in this study. |
| 8 | 参与条件与人数 / Eligibility and number | 18 岁以上，矫正视力正常，无癫痫史，未参与本系统的开发；约 30 人。/ Aged 18 or above, normal or corrected-to-normal vision, no history of epilepsy, not involved in developing the system; about 30 participants. |
| 9 | 参与的意义 / Benefits | [个人收益；对他人的意义] |
| 10 | 自愿参与与退出 / Voluntary participation | 完全自愿，可随时退出，无需说明理由，不受任何不利影响。/ Participation is voluntary. You may withdraw at any time without giving a reason and without any disadvantage. |
| 11 | 风险 / Risks | 少数人观看沉浸式 360° 视频时会有短暂不适或恶心，通常结束后很快消失；对光刺激敏感者可能诱发癫痫。如出现头晕、感知异常、眼部或肌肉抽动、四肢颤抖、方向感丧失或意识混乱，请立即告知主试。/ Some people feel temporary discomfort or nausea with immersive 360° video, which usually passes shortly after viewing. Certain visual stimuli can trigger seizures in susceptible people. Tell the experimenter immediately if you feel dizzy, notice odd perception, eye or muscle twitches, shivering limbs, disorientation or confusion. |
| 12 | 实验内容与采集的数据 / What is involved | 佩戴头显观看短片并评分；主试可能需要触碰头部以调整头显；记录评分、问卷回答和观看时的头部转动。/ You will watch short clips in a headset and give ratings. The experimenter may touch your head to adjust the headset. Ratings, questionnaire answers and head rotation during viewing are recorded. |
| 13 | 数据保管 / Data protection | 数据以编号保存，不含姓名；[保管人、保存期限、存放位置]。/ Data are stored under a participant code without names. [Custodian, retention period, storage.] |
| 14 | 报酬 / Compensation | [金额与方式；中途退出按约定支付（P.919 §11.4）] |
| 15 | 组织与资助 / Organiser and funding | [单位、项目] |
| 16 | 遵循的法规 / Legislation | [适用法规、IRB 批准号] |

## 2. 知情同意 / Consent（逐项勾选；改编自 P.919 附录 III 样例）

1. 我已阅读并理解信息页（文件编号 [ ]），有机会提问并得到满意答复。/ I have read and understood the information sheet [ID] and had the opportunity to ask questions.
2. 我理解参与是自愿的，可随时退出，无需说明理由，不受任何不利影响。/ I understand that participation is voluntary and that I may withdraw at any time without giving a reason and without disadvantage.
3. 我理解有癫痫发作史者不得参加本实验。/ I understand that people with a history of epileptic seizures may not take part.
4. 我同意按信息页所述目的处理我的个人信息。/ I consent to the processing of my personal information for the stated purposes.
5. 我理解研究团队会对我的身份保密，研究成果中不会出现可识别我的信息。/ I understand that my identity will be kept confidential and will not appear in any research output.
6. 我同意研究团队在科学报告和期刊中匿名使用我的评分数据。/ I consent to the anonymous use of my ratings in scientific reports and journals.
7. 我同意参加本研究。/ I agree to take part in this study.

（姓名与签名按 IRB 要求另行收集和保存，不进入 `pre_session.csv`。）

## 3. 基本信息与自报 / Background and self-report

| 题目 | 选项 | 导出列名 |
| --- | --- | --- |
| 被试编号（由主试提供）/ Participant code (given by the experimenter) | 文本，如 P07 | `participant_id` |
| 是否同意参加（第 2 节全部勾选）/ Consent given | 是 / 否 | `consent` |
| 年龄 / Age | 整数 | `age` |
| 性别 / Gender | 女 / 男 / 其他 / 不愿透露 | `gender` |
| VR 头显使用经验 / Experience with VR headsets | 从未 / 偶尔（少于 5 次）/ 经常 | `vr_experience` |
| 在佩戴日常眼镜或隐形眼镜的情况下，视力是否正常？/ Is your vision normal with your usual glasses or contact lenses? | 是 / 否 | `corrected_vision_normal` |
| 是否有已知的色觉异常（色弱、色盲）？/ Do you have any known colour vision deficiency? | 否 / 是（导出时按"色觉正常：是 / 否"记录） | `color_vision_normal` |
| 是否有已知的弱视、斜视或立体视问题？/ Do you have any known amblyopia, strabismus or stereo vision problem? | 否 / 是 | `binocular_problem` |
| 是否有癫痫发作史？/ Have you ever had an epileptic seizure? | 否 / 是（选"是"则不参加） | `epilepsy_history` |
| 测试时是否佩戴眼镜或隐形眼镜？/ Will you wear glasses or contact lenses during the test? | 框架眼镜 / 隐形眼镜 / 都不戴 | `eyewear` |

## 4. 导出要求

- 文件名 `pre_session.csv`，UTF-8，第一行为上表的导出列名；是否题统一导出为 `yes` / `no`。
- 放到采集数据目录（与各被试文件夹同级），或在分析时用 `--pre-session` 指定。
- 分析程序报告人口学统计、自报视力或色觉异常名单（默认只报告不剔除，剔除规则由用户在 `analysis_config.json` 中设定）、有实验数据但缺表单的被试。
