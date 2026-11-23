using System.Windows.Media;
using FocusFence.Core;

namespace FocusFence.App;

public sealed record TimeLimitRow(AppTimeLimit Limit, string UsageSummary, string StatusText, Brush StatusColor, string ToggleText);
