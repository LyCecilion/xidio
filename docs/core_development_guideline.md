# `xidio.Core` 开发准则

我们在这里引入一些 `xidio.Core` 的开发准则。围绕 `xidio.Core` 的开发必须遵循这些准则，以保证它可供 `xidio.CLI` 和 `xidio.GUI` 依赖。

## `xidio.Core` 不应依赖 `Console`、`Avalonia`、`Spectre`

`xidio.Core` 应负责且只应该负责探测、诊断、生成结构化结果、执行修复动作和上报。也就是说，`xidio.Core` 里不应当出现

```csharp
Console.WriteLine(...)
AnsiConsole.MarkupLine(...)
MessageBox.Show(...)
Avalonia.Threading.Dispatcher.UIThread...
```

## `xidio.Core` 应当输出结构化数据而非仅字符串

`xidio.Core` 的诊断、修复和上报请求和结果都应当作为结构化数据而非字符串。

## `xidio.Core` 的诊断、修复和上报逻辑必须区分

这一点是显然且必须做到的。

## `xidio.Core` 必须是异步的

首先，`xidio.Core` 不应当直接碰 UI 对象。

其次，`xidio.Core` 中所有的操作都必须是异步的。对于部分依赖系统 API 的操作，也应当使用恰当的方式封装为异步的。`xidio.Core` 需要支持进度事件，这样的事件可以帮助 CLI 和 GUI 渲染进度条。`xidio.Core` 的所有长任务都需要支持 `CancellationToken`，不能写阻塞等待。
