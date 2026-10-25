# 界面参考与第三方字体

2026-10-25 界面按 Shang 的最新要求调整为中性、极简、圆润字体和迷你悬浮按钮。

- [Reminders MenuBar](https://github.com/DamascenoRafael/reminders-menubar)：参考其小型弹出清单、简洁列表和弱化装饰的布局思路，没有复制 Swift 代码。
- [WPF UI](https://github.com/lepoco/wpfui)：调研其 WPF 控件、间距和主题组织方式；本程序使用自己的原生 WPF 样式，没有引入整套控件库。
- [Resource Han Rounded](https://github.com/CyanoHao/Resource-Han-Rounded)：实际使用简体中文 Bold 字体精简版（Plan Rounded，真实700字重），保留 7,849 个字符，包含 GB2312 常用中文；其他字形使用 Windows 字体回退。字体许可证为 SIL Open Font License，随附于 `Assets/Fonts/OFL-License.txt`。字体不安装到系统，只在本程序内使用，并以独立字体文件加载。

悬浮胶囊可见尺寸约 100 × 32 个 Windows 逻辑像素，外围另留透明阴影空间。默认出现在主屏右上角，可在主屏工作区内拖动，位置自动保存在本地。展开面板按任务文字和数量自动调整高度，长清单滚动显示。

按最新要求，小胶囊保留柔和彩色渐变，清单采用白色底和鲜艳随机文字颜色。字体加粗，日期含年份及中文星期并置于左右按钮之间居中，设置按钮在最右侧。输入框默认折叠成一个小加号。颜色随任务存储，不会每次刷新重新随机。内侧高光、细边框与柔和矢量阴影均由 WPF 绘制；没有原生背景模糊。采用可中断的临界阻尼弹簧衔接同一外壳，内容排版固定、后段淡入。没有后台循环动画。

为降低内存，使用不压缩目录发布、独立加载约3.01 MB的精简Bold字体、复用已排版内容、冻结共享画刷以及原生托盘，取消额外的Windows Forms和System.Drawing加载。小窗口默认软件绘制，在本机对比测试中降低私有内存。没有调用工作集清空接口或强制反复GC来美化占用数字。

动效参考 [Emil Apple Design](https://github.com/emilkowalski/skills/blob/main/skills/apple-design/SKILL.md) 与 [Animation Performance](https://github.com/emilkowalski/skills/blob/main/skills/animation-performance/SKILL.md)。借鉴连续性、输入可打断、源位置一致和固定内容排版原则，以原生WPF实现。

开发测试可带 `--ui-test` 参数启动，临时让窗口出现在任务栏供辅助功能工具识别；正常快捷方式启动不显示任务栏按钮。

日历版继续使用原生 WPF，没有新增日历或主题依赖。日期、加号、设置菜单和日历选中项使用同一组共享渐变画刷；菜单包含矢量图标与圆角开关。工作回顾改为周一开始的六周日历，选择日期查看右侧任务，窗口较小时隐藏格内预览以免裁字。最近7/30天以区间起始周排布，跨月天数完整呈现。历史保留原始安排日期，按完成时间重建每日状态，跨日延续不创建重复任务。
