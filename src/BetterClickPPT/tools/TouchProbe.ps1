param([string]$mode = 'seq', [int]$key = 0x20)
# Probe for PowerPoint "Touch/Mouse Mode" QAT toggle.
# ASCII-only source: Chinese element names are built from char codes.
$ErrorActionPreference = 'Continue'

$cs = @'
public static class KProbe {
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
  public struct MI { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public System.IntPtr extra; }
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
  public struct KU { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public System.IntPtr extra; }
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
  public struct UN { [System.Runtime.InteropServices.FieldOffset(0)] public MI mi; [System.Runtime.InteropServices.FieldOffset(0)] public KU ki; }
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
  public struct IK { public uint type; public UN u; }
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint SendInput(uint n, IK[] a, int sz);
  public delegate bool EP(System.IntPtr h, System.IntPtr l);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumChildWindows(System.IntPtr p, EP cb, System.IntPtr l);
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(System.IntPtr h, System.Text.StringBuilder s, int n);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RC r);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern System.IntPtr SetProcessDpiAwarenessContext(System.IntPtr v);
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern System.IntPtr FindWindowW(string cls, string title);
  public struct RC { public int L; public int T; public int R; public int B; }
  public static long Dpi() { return (long)SetProcessDpiAwarenessContext((System.IntPtr)(-4)); }
  public static System.IntPtr Fw(string c) { return FindWindowW(c, null); }
  public static uint Key(ushort vk, bool up) {
    var a = new IK[1]; a[0].type = 1; a[0].u.ki.wVk = vk; a[0].u.ki.dwFlags = up ? 2u : 0u;
    return SendInput(1, a, System.Runtime.InteropServices.Marshal.SizeOf(typeof(IK)));
  }
  public static string Tap(ushort vk) { return Key(vk, false) + "," + Key(vk, true); }
  public static string AltKey(ushort vk) { return Key(0x12, false) + "," + Key(vk, false) + "," + Key(vk, true) + "," + Key(0x12, true); }
  public static string Docks(System.IntPtr p) {
    var sb2 = new System.Text.StringBuilder();
    EnumChildWindows(p, delegate(System.IntPtr h, System.IntPtr l) {
      var sb = new System.Text.StringBuilder(256); GetClassName(h, sb, 256);
      if (sb.ToString() == "MsoCommandBarDock") { RC r; GetWindowRect(h, out r); sb2.Append("[" + r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T) + "] "); }
      return true; }, System.IntPtr.Zero);
    return sb2.ToString();
  }
  public static int[] BigDock(System.IntPtr p) {
    var res = new int[] { 0, 0, 0, 0 }; long ba = 0;
    EP cb = delegate(System.IntPtr h, System.IntPtr l) {
      var sb = new System.Text.StringBuilder(256); GetClassName(h, sb, 256);
      if (sb.ToString() == "MsoCommandBarDock") {
        RC r; GetWindowRect(h, out r); long a = (long)(r.R - r.L) * (r.B - r.T);
        if (a > ba) { ba = a; res[0] = r.L; res[1] = r.T; res[2] = r.R - r.L; res[3] = r.B - r.T; }
      }
      return true; };
    EnumChildWindows(p, cb, System.IntPtr.Zero);
    return res;
  }
}
'@

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition $cs
$inj = 'C:\Users\Admin\Desktop\LTFC\src\BetterClickPPT\tools\BcppInjector.cs'
if (Test-Path $inj) { Add-Type -Path $inj }

# char codes: 0x89E6 0x6478 '/' 0x9F20 0x6807 0x6A21 0x5F0F  == touch/mouse mode label
$script:TouchName = ([string][char]0x89E6) + [char]0x6478 + '/' + [char]0x9F20 + [char]0x6807 + [char]0x6A21 + [char]0x5F0F
# char codes: 0x5FEB 0x901F 0x8BBF 0x95EE 0x5DE5 0x5177 0x680F  == quick access toolbar
$script:QatName = ([string][char]0x5FEB) + [char]0x901F + [char]0x8BBF + [char]0x95EE + [char]0x5DE5 + [char]0x5177 + [char]0x680F

function Get-Hash([int]$x, [int]$y, [int]$w, [int]$h) {
  if ($w -le 0 -or $h -le 0) { return 'ZERO' }
  try {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $bmp.Dispose()
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $bytes = $md5.ComputeHash($ms.ToArray())
    $ms.Dispose()
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '').Substring(0, 10)
  } catch { return 'ERR' }
}

function Get-Snap([System.IntPtr]$h) {
  $d = [KProbe]::BigDock($h)
  [PSCustomObject]@{
    Docks    = [KProbe]::Docks($h)
    DockH    = $d[3]
    DockW    = $d[2]
    DockHash = (Get-Hash $d[0] $d[1] $d[2] $d[3])
  }
}

function Get-TouchElem($win) {
  $AC = [System.Windows.Automation.AutomationElement]
  $nc = New-Object System.Windows.Automation.PropertyCondition($AC::NameProperty, $script:TouchName)
  return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nc)
}

function Write-Snap([string]$tag, $snap) {
  Write-Output ("{0} dockH={1} dockW={2} hash={3} docks={4}" -f $tag, $snap.DockH, $snap.DockW, $snap.DockHash, $snap.Docks)
}

# --- locate target -------------------------------------------------------
Write-Output ('dpi=' + [KProbe]::Dpi())
$hwnd = [KProbe]::Fw('PPTFrameClass')
Write-Output ('hwnd=' + $hwnd)
if ($hwnd -eq [System.IntPtr]::Zero) { Write-Output 'NO PPT FRAME'; exit 2 }
if (Test-Path $inj) { [BcppInjector]::ForceForeground($hwnd) | Out-Null }
Start-Sleep -Milliseconds 700
Write-Output ('fg=' + [KProbe]::GetForegroundWindow())

$AC = [System.Windows.Automation.AutomationElement]
$cond = [System.Windows.Automation.Condition]::TrueCondition
$win = $null
foreach ($c in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  if ($c.Current.ClassName -eq 'PPTFrameClass') { $win = $c }
}
if (-not $win) { Write-Output 'NO FRAME ELEMENT'; exit 3 }
Write-Output ('framePid=' + $win.Current.ProcessId)
$t = Get-TouchElem $win
if (-not $t) { Write-Output 'NO TOUCH ELEMENT'; exit 4 }
$br = $t.Current.BoundingRectangle
Write-Output ('elem=' + $t.Current.ClassName + ' rect=' + [int]$br.X + ',' + [int]$br.Y + ' ' + [int]$br.Width + 'x' + [int]$br.Height + ' accesskey=' + $t.Current.AccessKey + ' focus=' + $t.Current.HasKeyboardFocus)
Write-Output ('btnHash=' + (Get-Hash ([int]$br.X) ([int]$br.Y) ([int]$br.Width) ([int]$br.Height)))

$s0 = Get-Snap $hwnd
Write-Snap 'S0' $s0

# --- activation attempts -------------------------------------------------
$steps = @()
if ($mode -eq 'seq') {
  $steps += @{ Name = 'setfocus'; Act = { $t.SetFocus(); 'ok' } }
  $steps += @{ Name = 'space'; Act = { [KProbe]::Tap(0x20) } }
  $steps += @{ Name = 'enter'; Act = { [KProbe]::Tap(0x0D) } }
  $steps += @{ Name = 'keytip5'; Act = { [KProbe]::Tap(0x12) + '/'; Start-Sleep -Milliseconds 800; [KProbe]::Tap(0x35) } }
  $steps += @{ Name = 'alt5'; Act = { [KProbe]::AltKey(0x35) } }
} elseif ($mode -eq 'popup') {
  $steps += @{ Name = 'expand'; Act = { $ecp = $t.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern); $ecp.Expand(); 'state=' + $ecp.Current.ExpandCollapseState } }
} elseif ($mode -eq 'key') {
  $steps += @{ Name = ('key_' + [Convert]::ToString($key, 16)); Act = { [KProbe]::Tap([ushort]$key) } }
}

$i = 0
foreach ($s in $steps) {
  $i++
  try { $r = & $s.Act } catch { $r = 'ERR:' + $_.Exception.Message }
  Write-Output ('--- step ' + $i + ' ' + $s.Name + ' -> ' + $r)
  Start-Sleep -Milliseconds 2500
  $sn = Get-Snap $hwnd
  Write-Snap ('S' + $i + '-' + $s.Name) $sn
  $changed = ($sn.DockHash -ne $s0.DockHash) -or ($sn.DockH -ne $s0.DockH)
  Write-Output ('CHANGED=' + $changed)
  $br2 = $t.Current.BoundingRectangle
  Write-Output ('elemFocusNow=' + $t.Current.HasKeyboardFocus + ' btnHash=' + (Get-Hash ([int]$br2.X) ([int]$br2.Y) ([int]$br2.Width) ([int]$br2.Height)))
}
if ($mode -eq 'popup') {
  Write-Output '--- popup tree ---'
  $menus = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AC::ControlTypeProperty, [System.Windows.Automation.ControlType]::Menu)))
  Write-Output ('menucount=' + $menus.Count)
  foreach ($m in $menus) {
    Write-Output ('MENU ' + $m.Current.ClassName + ' ' + $m.Current.BoundingRectangle)
    foreach ($k in $m.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
      Write-Output ('  child ' + $k.Current.ControlType.ProgrammaticName + ' name=' + $k.Current.Name + ' accesskey=' + $k.Current.AccessKey)
    }
  }
  Start-Sleep -Milliseconds 300
  $t.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  Write-Output 'collapsed'
}
if ($mode -eq 'show') {
  function Show-Tops([string]$tag) {
    Write-Output ("{0} fg={1}" -f $tag, [KProbe]::GetForegroundWindow())
    foreach ($c in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
      $r = $c.Current.BoundingRectangle
      Write-Output ("  top {0} name={1} rect={2},{3} {4}x{5}" -f $c.Current.ClassName, $c.Current.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    }
  }
  Write-Output '=== start slideshow (F5) ==='
  Write-Output ('f5ret=' + [KProbe]::Tap(0x74))
  Start-Sleep -Seconds 4
  Show-Tops 'SHOW'
  Write-Snap 'S-show' (Get-Snap $hwnd)
  $t3 = Get-TouchElem $win
  if ($t3) {
    Write-Output ('whileShow elem=' + $t3.Current.BoundingRectangle.ToString() + ' offscreen=' + $t3.Current.IsOffscreen)
    try { $t3.SetFocus(); Write-Output 'whileShow setfocus=ok' } catch { Write-Output ('whileShow setfocus=ERR:' + $_.Exception.Message) }
    Start-Sleep -Milliseconds 500
    Write-Output ('whileShow focusNow=' + $t3.Current.HasKeyboardFocus)
    Write-Output ('whileShow enter=' + [KProbe]::Tap(0x0D))
    Start-Sleep -Seconds 2
    Write-Snap 'S-show-enter' (Get-Snap $hwnd)
  } else { Write-Output 'whileShow elem=NULL' }
  Write-Output ('esc=' + [KProbe]::Tap(0x1B))
  Start-Sleep -Seconds 3
  Show-Tops 'AFTER-ESC'
  Write-Snap 'S-aftershow' (Get-Snap $hwnd)
  Write-Output 'show-test-done'
}
if ($mode -eq 'one') {
  function Act-Toggle([string]$tag) {
    $e = Get-TouchElem $win
    if (-not $e) { Write-Output ("{0}: NO ELEM" -f $tag); return }
    $r1 = $e.Current.BoundingRectangle
    Write-Output ("{0}: rect={1} {2}x{3} enabled={4} offscreen={5} key={6}" -f $tag, [int]$r1.X, [int]$r1.Y, [int]$r1.Width, [int]$r1.Height, $e.Current.IsEnabled, $e.Current.IsOffscreen, $e.Current.AccessKey)
    try { $e.SetFocus(); Write-Output ("{0}: setfocus=ok" -f $tag) } catch { Write-Output ("{0}: setfocus=ERR {1}" -f $tag, $_.Exception.Message) }
    Start-Sleep -Milliseconds 600
    Write-Output ("{0}: focusNow={1}" -f $tag, $e.Current.HasKeyboardFocus)
    Write-Output ("{0}: enter={1}" -f $tag, [KProbe]::Tap(0x0D))
    Start-Sleep -Seconds 3
    Write-Snap ($tag + '-after') (Get-Snap $hwnd)
  }
  Act-Toggle 'T1'
  Act-Toggle 'T2'
  Act-Toggle 'T3'
}
if ($mode -eq 'k') {
  Write-Output ('== key probe 0x' + [Convert]::ToString($key, 16) + ' ==')
  for ($n = 1; $n -le 3; $n++) {
    $e = Get-TouchElem $win
    $w0 = -1; if ($e) { $w0 = [int]$e.Current.BoundingRectangle.Width }
    $sn = Get-Snap $hwnd
    Write-Output ("K{0}-before dockH={1} btnw={2}" -f $n, $sn.DockH, $w0)
    Write-Output ("K{0}-docks-before {1}" -f $n, $sn.Docks)
    if ($e) {
      try { $e.SetFocus(); Write-Output ("K{0} setfocus=ok focusNow={1}" -f $n, $e.Current.HasKeyboardFocus) } catch { Write-Output ("K{0} setfocus=ERR {1}" -f $n, $_.Exception.Message) }
    }
    Start-Sleep -Milliseconds 500
    Write-Output ("K{0} tap={1}" -f $n, [KProbe]::Tap([uint16]$key))
    Start-Sleep -Milliseconds 3000
    Write-Output ("K{0}-t3 dockH={1}" -f $n, (Get-Snap $hwnd).DockH)
    Start-Sleep -Milliseconds 3000
    $e2 = Get-TouchElem $win
    $w1 = -1; if ($e2) { $w1 = [int]$e2.Current.BoundingRectangle.Width }
    $sn2 = Get-Snap $hwnd
    Write-Output ("K{0}-t6 dockH={1} btnw={2}" -f $n, $sn2.DockH, $w1)
    Write-Output ("K{0}-docks-after {1}" -f $n, $sn2.Docks)
  }
}
$cs2 = @'
public static class WTProbe {
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
  public struct RC { public int L; public int T; public int R; public int B; }
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
  public struct PT { public int X; public int Y; }
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RC r);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetParent(System.IntPtr h);
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetClassName(System.IntPtr h, System.Text.StringBuilder s, int n);
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr h, System.Text.StringBuilder s, int n);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern int GetDlgCtrlID(System.IntPtr h);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowEnabled(System.IntPtr h);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ClientToScreen(System.IntPtr h, ref PT p);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ScreenToClient(System.IntPtr h, ref PT p);
  public delegate bool EP(System.IntPtr h, System.IntPtr l);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumChildWindows(System.IntPtr p, EP cb, System.IntPtr l);
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern System.IntPtr SendMessageTimeoutW(System.IntPtr h, uint msg, System.IntPtr wp, System.IntPtr lp, uint flags, uint ms, out System.IntPtr res);
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool PostMessageW(System.IntPtr h, uint msg, System.IntPtr wp, System.IntPtr lp);
  public static string Dump(System.IntPtr root) {
    var sb = new System.Text.StringBuilder();
    EnumChildWindows(root, delegate(System.IntPtr h, System.IntPtr l) {
      RC r; GetWindowRect(h, out r);
      var cn = new System.Text.StringBuilder(128); GetClassName(h, cn, 128);
      var tx = new System.Text.StringBuilder(128); GetWindowText(h, tx, 128);
      int d = 0; System.IntPtr p = GetParent(h);
      while (p != System.IntPtr.Zero && p != root) { d++; p = GetParent(p); }
      sb.Append("d" + d + " h=" + h + " id=" + GetDlgCtrlID(h) + " vis=" + (IsWindowVisible(h) ? 1 : 0) + " en=" + (IsWindowEnabled(h) ? 1 : 0)
        + " [" + r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T) + "] " + cn + " '" + tx + "'\n");
      return true; }, System.IntPtr.Zero);
    return sb.ToString();
  }
  // WM_MOUSEMOVE(0x200)+WM_LBUTTONDOWN(0x201)+WM_LBUTTONUP(0x202) via SendMessageTimeout (hook-free).
  public static string ClickCenter(System.IntPtr h, uint ms, bool moveFirst) {
    RC r; GetWindowRect(h, out r);
    PT p; p.X = 0; p.Y = 0; ClientToScreen(h, ref p);
    int cx = (r.L + r.R) / 2 - p.X, cy = (r.T + r.B) / 2 - p.Y;
    int lp = ((cy & 0xFFFF) << 16) | (cx & 0xFFFF);
    var res = System.IntPtr.Zero;
    string pre = "-";
    if (moveFirst) { pre = SendMessageTimeoutW(h, 0x0200, System.IntPtr.Zero, (System.IntPtr)lp, 2, ms, out res).ToString(); }
    var a = SendMessageTimeoutW(h, 0x0201, (System.IntPtr)1, (System.IntPtr)lp, 2, ms, out res);
    var b = SendMessageTimeoutW(h, 0x0202, System.IntPtr.Zero, (System.IntPtr)lp, 2, ms, out res);
    return "move=" + pre + " down=" + a + " up=" + b + " client=" + cx + "," + cy;
  }
  public static string PostClickCenter(System.IntPtr h) {
    RC r; GetWindowRect(h, out r);
    PT p; p.X = 0; p.Y = 0; ClientToScreen(h, ref p);
    int cx = (r.L + r.R) / 2 - p.X, cy = (r.T + r.B) / 2 - p.Y;
    int lp = ((cy & 0xFFFF) << 16) | (cx & 0xFFFF);
    bool m = PostMessageW(h, 0x0200, System.IntPtr.Zero, (System.IntPtr)lp);
    bool a = PostMessageW(h, 0x0201, (System.IntPtr)1, (System.IntPtr)lp);
    bool b = PostMessageW(h, 0x0202, System.IntPtr.Zero, (System.IntPtr)lp);
    return "move=" + m + " down=" + a + " up=" + b + " client=" + cx + "," + cy;
  }
  // Click a point given in SCREEN coordinates on window h (converted to client coords). Hook-free.
  public static string ClickScreenPoint(System.IntPtr h, int sx, int sy, uint ms, bool moveFirst) {
    PT p; p.X = sx; p.Y = sy; ScreenToClient(h, ref p);
    int cx = p.X, cy = p.Y;
    int lp = ((cy & 0xFFFF) << 16) | (cx & 0xFFFF);
    var res = System.IntPtr.Zero;
    string pre = "-";
    if (moveFirst) { pre = SendMessageTimeoutW(h, 0x0200, System.IntPtr.Zero, (System.IntPtr)lp, 2, ms, out res).ToString(); }
    var a = SendMessageTimeoutW(h, 0x0201, (System.IntPtr)1, (System.IntPtr)lp, 2, ms, out res);
    var b = SendMessageTimeoutW(h, 0x0202, System.IntPtr.Zero, (System.IntPtr)lp, 2, ms, out res);
    return "move=" + pre + " down=" + a + " up=" + b + " client=" + cx + "," + cy;
  }
  public static string PostScreenPoint(System.IntPtr h, int sx, int sy) {
    PT p; p.X = sx; p.Y = sy; ScreenToClient(h, ref p);
    int cx = p.X, cy = p.Y;
    int lp = ((cy & 0xFFFF) << 16) | (cx & 0xFFFF);
    bool m = PostMessageW(h, 0x0200, System.IntPtr.Zero, (System.IntPtr)lp);
    bool a = PostMessageW(h, 0x0201, (System.IntPtr)1, (System.IntPtr)lp);
    bool b = PostMessageW(h, 0x0202, System.IntPtr.Zero, (System.IntPtr)lp);
    return "move=" + m + " down=" + a + " up=" + b + " client=" + cx + "," + cy;
  }
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint MapVirtualKeyW(uint code, uint mapType);
  // Hook-free key press: posts WM_KEYDOWN/UP (with real scan code) to the given window, like a real keystroke.
  public static string PostKey(System.IntPtr h, uint vk, int dwellMs) {
    uint sc = MapVirtualKeyW(vk, 0);
    int lpDown = 1 | ((int)sc << 16);
    int lpUp = 1 | ((int)sc << 16) | (1 << 30) | unchecked((int)0x80000000);
    bool a = PostMessageW(h, 0x0100, (System.IntPtr)(int)vk, (System.IntPtr)lpDown);
    if (dwellMs > 0) { System.Threading.Thread.Sleep(dwellMs); }
    bool b = PostMessageW(h, 0x0101, (System.IntPtr)(int)vk, (System.IntPtr)lpUp);
    return "down=" + a + " up=" + b + " sc=" + sc;
  }
  public struct RC4 { public int L; public int T; public int R; public int B; }
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
  public struct GUI { public int cbSize; public uint flags; public System.IntPtr hwndActive; public System.IntPtr hwndFocus; public System.IntPtr hwndCapture; public System.IntPtr hwndMenuOwner; public System.IntPtr hwndMoveSize; public System.IntPtr hwndCaret; public RC4 rcCaret; }
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint tid, ref GUI g);
  [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetWindowThreadProcessId(System.IntPtr h, System.IntPtr pid);
  // Which HWND currently owns keyboard focus for that window's thread (posted keys must target it).
  public static string FocusHwnd(System.IntPtr h) {
    GUI g = new GUI(); g.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(GUI));
    uint tid = GetWindowThreadProcessId(h, System.IntPtr.Zero);
    bool ok = GetGUIThreadInfo(tid, ref g);
    return "ok=" + ok + " focus=" + g.hwndFocus + " active=" + g.hwndActive;
  }
}
'@
if ($mode -eq 'wt' -or $mode -eq 'nd' -or $mode -eq 'act') { Add-Type -TypeDefinition $cs2 }
if ($mode -eq 'wt') {
  $brT = $t.Current.BoundingRectangle
  $bx0 = [int]$brT.X; $by0 = [int]$brT.Y; $bx1 = $bx0 + [int]$brT.Width; $by1 = $by0 + [int]$brT.Height
  Write-Output ("btnrect={0},{1} {2}x{3}" -f $bx0, $by0, [int]$brT.Width, [int]$brT.Height)

  Write-Output '--- supported patterns ---'
  foreach ($pt in $t.GetSupportedPatterns()) { Write-Output ('  pat ' + $pt.ProgrammaticName) }
  Write-Output ('  invokeAvail=' + $t.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsInvokePatternAvailableProperty))

  $tree = [WTProbe]::Dump($hwnd)
  Write-Output '--- child window tree (PPT frame) ---'
  Write-Output $tree

  Write-Output '--- candidates intersecting button rect ---'
  $cands = @()
  foreach ($line in ($tree -split "`n")) {
    if ($line -notmatch '\[(-?\d+),(-?\d+) (\d+)x(\d+)\]') { continue }
    $lx = [int]$Matches[1]; $ly = [int]$Matches[2]; $lw = [int]$Matches[3]; $lh = [int]$Matches[4]
    if ($lw -le 0 -or $lh -le 0) { continue }
    $cx = $lx + [int]($lw / 2); $cy = $ly + [int]($lh / 2)
    if ($cx -ge $bx0 -and $cx -le $bx1 -and $cy -ge $by0 -and $cy -le $by1) {
      Write-Output ('  CAND ' + $line)
      if ($line -match 'h=(\d+)') { $cands += [int64]$Matches[1] }
    }
  }
  Write-Output ('candcount=' + $cands.Count)

  foreach ($ch in $cands) {
    $hch = [System.IntPtr]$ch
    Write-Output ('--- click candidate h=' + $ch + ' ---')
    Write-Output ('SMSEND ' + [WTProbe]::ClickCenter($hch, 2000, $true))
    Start-Sleep -Seconds 3
    $snA = Get-Snap $hwnd
    Write-Output ("A dockH={0} docks={1}" -f $snA.DockH, $snA.Docks)
    Write-Output ('POST ' + [WTProbe]::PostClickCenter($hch))
    Start-Sleep -Seconds 3
    $snB = Get-Snap $hwnd
    Write-Output ("B dockH={0} docks={1}" -f $snB.DockH, $snB.Docks)
  }
}
if ($mode -eq 'act') {
  $touchLabel = ([string][char]0x89E6) + [char]0x6478 + [char]0x6A21 + [char]0x5F0F
  $mouseLabel = ([string][char]0x9F20) + [char]0x6807 + [char]0x6A21 + [char]0x5F0F
  $script:Pops = @()
  function Get-State {
    $e = Get-TouchElem $win
    $b = $e.Current.BoundingRectangle
    $sn = Get-Snap $hwnd
    return [PSCustomObject]@{ W = [int]$b.Width; H = [int]$b.Height; DockH = $sn.DockH; DockW = $sn.DockW; Hash = $sn.DockHash }
  }
  function Show-State([string]$tag) {
    $s = Get-State
    Write-Output ("{0} btnW={1} btnH={2} dockH={3} hash={4}" -f $tag, $s.W, $s.H, $s.DockH, $s.Hash)
  }
  function Show-Popups {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $procId = $win.Current.ProcessId
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
      if ($w.Current.ProcessId -ne $procId -or $w.Current.ClassName -eq 'PPTFrameClass') { continue }
      $script:Pops += $w
      $r = $w.Current.BoundingRectangle
      Write-Output ("  win " + $w.Current.ClassName + ' name=' + $w.Current.Name + ' rect=' + [int]$r.X + ',' + [int]$r.Y + ' ' + [int]$r.Width + 'x' + [int]$r.Height)
      $n = 0
      foreach ($d in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($n -ge 20) { break }
        $p = @(); foreach ($pp in $d.GetSupportedPatterns()) { $p += $pp.ProgrammaticName.Replace('PatternIdentifiers.Pattern', '') }
        Write-Output ("    it " + $d.Current.ControlType.ProgrammaticName.Replace('ControlType.', '') + ' name=' + $d.Current.Name + ' cls=' + $d.Current.ClassName + ' pat=' + ($p -join ','))
        $n++
      }
    }
  }
  Show-State 'A0'
  Write-Output '--- 1 focus + posted SPACE ---'
  try { $t.SetFocus(); Start-Sleep -Milliseconds 400; Write-Output ('  focusNow=' + $t.Current.HasKeyboardFocus) } catch { Write-Output ('  ERR ' + $_.Exception.Message) }
  Write-Output ('  gui=' + [WTProbe]::FocusHwnd($hwnd))
  Write-Output ('  post ' + [WTProbe]::PostKey($hwnd, 0x20, 150))
  Start-Sleep -Seconds 3
  Show-State 'A1-post-space'
  Write-Output '--- 2 focus + posted RETURN ---'
  try { $t.SetFocus(); Start-Sleep -Milliseconds 400 } catch { }
  Write-Output ('  post ' + [WTProbe]::PostKey($hwnd, 0x0D, 150))
  Start-Sleep -Seconds 3
  Show-State 'A2-post-return'
  Write-Output '--- 3 focus + posted SPACE+RETURN ---'
  try { $t.SetFocus(); Start-Sleep -Milliseconds 400 } catch { }
  Write-Output ('  post ' + [WTProbe]::PostKey($hwnd, 0x20, 150))
  Start-Sleep -Milliseconds 800
  Write-Output ('  post ' + [WTProbe]::PostKey($hwnd, 0x0D, 150))
  Start-Sleep -Seconds 3
  Show-State 'A3-post-pair'
  Write-Output '--- 4 control: focus + SendInput SPACE+RETURN ---'
  try { $t.SetFocus(); Start-Sleep -Milliseconds 400 } catch { }
  [KProbe]::Key(0x20, $false); Start-Sleep -Milliseconds 110; [KProbe]::Key(0x20, $true)
  Start-Sleep -Milliseconds 800
  [KProbe]::Key(0x0D, $false); Start-Sleep -Milliseconds 110; [KProbe]::Key(0x0D, $true)
  Start-Sleep -Seconds 3
  Show-State 'A4-sendinput-pair'
  Write-Output '--- 5 legacy default action ---'
  $lp = $null
  try { $lp = $t.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern) } catch { Write-Output ('  GET ERR ' + $_.Exception.Message) }
  if ($lp) {
    try { Write-Output ('  action=' + $lp.Current.DefaultAction + ' state=' + $lp.Current.State + ' role=' + $lp.Current.Role) } catch { Write-Output ('  CUR ERR ' + $_.Exception.Message) }
    try { $lp.DoDefaultAction(); Write-Output '  DoDefaultAction sent' } catch { Write-Output ('  DO ERR ' + $_.Exception.Message) }
    Start-Sleep -Seconds 3
    Show-State 'A5-legacy-default'
  }
  Write-Output '--- 6 expand + menu items ---'
  try {
    $ec = $t.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    Write-Output ('  state=' + $ec.Current.ExpandCollapseState)
    $ec.Expand()
    Write-Output '  Expand sent'
  } catch { Write-Output ('  ERR ' + $_.Exception.Message) }
  Start-Sleep -Milliseconds 1600
  Write-Output ('  gui=' + [WTProbe]::FocusHwnd($hwnd))
  $script:Pops = @()
  Show-Popups | Out-Null
  $pops = @($script:Pops)
  Start-Sleep -Seconds 1
  Show-State 'A6-after-expand'
  foreach ($p in $pops) {
    foreach ($d in $p.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
      if ($d.Current.Name -ne $touchLabel -and $d.Current.Name -ne $mouseLabel) { continue }
      Write-Output ('  ITEM ' + $d.Current.Name + ' cls=' + $d.Current.ClassName)
      foreach ($pn in $d.GetSupportedPatterns()) {
        $nm = $pn.ProgrammaticName.Replace('PatternIdentifiers.Pattern', '')
        Write-Output ('    pattern ' + $nm)
        try {
          if ($nm -eq 'InvokePattern') { $d.GetCurrentPattern($pn).Invoke() }
          elseif ($nm -eq 'SelectionItemPattern') { $d.GetCurrentPattern($pn).Select() }
          elseif ($nm -eq 'LegacyIAccessiblePattern') { $d.GetCurrentPattern($pn).DoDefaultAction() }
          else { continue }
          Write-Output ('    used ' + $nm)
          Start-Sleep -Seconds 3
          Show-State ('after-item-' + $nm)
        } catch { Write-Output ('    ERR ' + $_.Exception.Message) }
      }
    }
  }
}
if ($mode -eq 'nd') {
  $brT = $t.Current.BoundingRectangle
  $bx = [int]($brT.X + $brT.Width / 2); $by = [int]($brT.Y + $brT.Height / 2)
  Write-Output ("btncenter={0},{1} btnw={2}" -f $bx, $by, [int]$brT.Width)
  $tree = [WTProbe]::Dump($hwnd)
  $hits = @()
  foreach ($line in ($tree -split "`n")) {
    if ($line -notmatch '^(d\d+) h=(\d+) id=\d+ vis=(\d+) en=(\d+) \[(-?\d+),(-?\d+) (\d+)x(\d+)\]') { continue }
    $dep = [int]$Matches[1].Substring(1)
    $hh = [int64]$Matches[2]; $vis = [int]$Matches[3]
    $lx = [int]$Matches[5]; $ly = [int]$Matches[6]; $lw = [int]$Matches[7]; $lh = [int]$Matches[8]
    if ($vis -ne 1 -or $lw -le 0 -or $lh -le 0) { continue }
    if ($bx -lt $lx -or $bx -gt ($lx + $lw) -or $by -lt $ly -or $by -gt ($ly + $lh)) { continue }
    $hits += [PSCustomObject]@{ Depth = $dep; H = $hh; Line = $line }
  }
  $hits = @($hits | Sort-Object Depth -Descending)
  Write-Output ('hits=' + $hits.Count)
  foreach ($hit in $hits) { Write-Output ('  HIT ' + $hit.Line) }
  foreach ($hit in $hits) {
    Write-Output ('--- SMSEND screen-click on h=' + $hit.H + ' ---')
    try { Write-Output ('  ret ' + [WTProbe]::ClickScreenPoint([System.IntPtr]$hit.H, $bx, $by, 2000, $true)) } catch { Write-Output ('  ERR ' + $_.Exception.Message) }
    Start-Sleep -Seconds 3
    $sn = Get-Snap $hwnd
    Write-Output ("  after dockH={0} docks={1}" -f $sn.DockH, $sn.Docks)
    Write-Output ('--- POST screen-click on h=' + $hit.H + ' ---')
    try { Write-Output ('  ret ' + [WTProbe]::PostScreenPoint([System.IntPtr]$hit.H, $bx, $by)) } catch { Write-Output ('  ERR ' + $_.Exception.Message) }
    Start-Sleep -Seconds 3
    $sn2 = Get-Snap $hwnd
    Write-Output ("  after dockH={0} docks={1}" -f $sn2.DockH, $sn2.Docks)
  }
}
Write-Output 'done'
