# Discover and test the Office ribbon (MSO) command id of the "Touch/Mouse Mode" toggle.
# Hook-free path: PowerPoint COM automation (Application.CommandBars.ExecuteMso) - no input injection.
# ASCII-only source: Chinese labels are built from char codes.
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$cs = @'
public static class TM {
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct RC { public int L; public int T; public int R; public int B; }
  public delegate bool EP(System.IntPtr h, System.IntPtr l);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumChildWindows(System.IntPtr p, EP cb, System.IntPtr l);
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(System.IntPtr h, System.Text.StringBuilder s, int n);
  [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RC r);
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern System.IntPtr FindWindowW(string c, string t);
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
        RC r; GetWindowRect(h, out r);
        long a = (long)(r.R - r.L) * (r.B - r.T);
        if (a > ba) { ba = a; res[0] = r.L; res[1] = r.T; res[2] = r.R - r.L; res[3] = r.B - r.T; }
      }
      return true; };
    EnumChildWindows(p, cb, System.IntPtr.Zero);
    return res;
  }
}
'@
Add-Type -TypeDefinition $cs

# char codes: 0x89E6 0x6478 '/' 0x9F20 0x6807 0x6A21 0x5F0F
$TouchName = ([string][char]0x89E6) + [char]0x6478 + '/' + [char]0x9F20 + [char]0x6807 + [char]0x6A21 + [char]0x5F0F

$hwnd = [TM]::FindWindowW('PPTFrameClass', $null)
Write-Output ('hwnd=' + $hwnd)
$cond = [System.Windows.Automation.Condition]::TrueCondition
$win = $null
foreach ($c in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
  if ($c.Current.ClassName -eq 'PPTFrameClass') { $win = $c }
}
if (-not $win) { Write-Output 'NO FRAME'; exit 3 }
$nc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $TouchName)

function Get-State {
  $e = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nc)
  $w = 0; $h = 0; $aid = ''
  if ($e) { $b = $e.Current.BoundingRectangle; $w = [int]$b.Width; $h = [int]$b.Height; $aid = $e.Current.AutomationId }
  $d = [TM]::BigDock($hwnd)
  return [PSCustomObject]@{ BtnW = $w; BtnH = $h; Aid = $aid; DockH = $d[3]; Docks = [TM]::Docks($hwnd) }
}
function Show-State([string]$tag) {
  $s = Get-State
  Write-Output ("{0} btnW={1} btnH={2} aid={3} dockH={4}" -f $tag, $s.BtnW, $s.BtnH, $s.Aid, $s.DockH)
}
function Codes($s) { $r = @(); foreach ($ch in $s.ToCharArray()) { $r += ('0x{0:X4}' -f [int]$ch) }; return ($r -join ',') }
Show-State 'M0'

# --- attach to the running PowerPoint instance (avoid starting a second one) ---
$app = $null
try { $app = [System.Runtime.InteropServices.Marshal]::GetActiveObject('PowerPoint.Application'); Write-Output 'attached via ROT' }
catch { Write-Output ('ROT miss: ' + $_.Exception.Message) }
if (-not $app) {
  try { $app = New-Object -ComObject PowerPoint.Application; Write-Output 'created via COM' } catch { Write-Output ('COM ERR ' + $_.Exception.Message) }
}
if (-not $app) { Write-Output 'NO COM APP'; exit 4 }
Write-Output ('app=' + $app.Name + ' ver=' + $app.Version + ' vis=' + $app.Visible + ' pres=' + $app.Presentations.Count + ' shows=' + $app.SlideShowWindows.Count)
$cb = $app.CommandBars
Write-Output ('commandbars=' + $cb.Count)

$ids = @('TouchMode', 'TouchModeToggle', 'MouseMode', 'TouchMouseMode', 'ToggleTouchMode', 'TouchMouseModeToggle', 'TouchModeToggleButton', 'Touch', 'MouseOrTouchMode', 'TouchMouse')
foreach ($id in $ids) {
  $label = '<none>'
  try { $label = [string]$cb.GetLabelMso($id) } catch { $label = 'ERR:' + $_.Exception.Message }
  $vis = '-'
  try { $vis = [string]$cb.GetVisibleMso($id) } catch { $vis = 'ERR' }
  $en = '-'
  try { $en = [string]$cb.GetEnabledMso($id) } catch { $en = 'ERR' }
  $pr = '-'
  try { $pr = [string]$cb.GetPressedMso($id) } catch { $pr = 'ERR' }
  Write-Output ("id={0} label=[{1}] codes={2} vis={3} en={4} pressed={5}" -f $id, $label, (Codes $label), $vis, $en, $pr)
}

foreach ($id in $ids) {
  $label = ''
  try { $label = [string]$cb.GetLabelMso($id) } catch { continue }
  if ($label -ne $TouchName) { continue }
  Write-Output ('=== ExecuteMso ' + $id + ' ===')
  try { $cb.ExecuteMso($id); Write-Output '  executed' } catch { Write-Output ('  ERR ' + $_.Exception.Message) }
  Start-Sleep -Seconds 3
  Show-State ('after-' + $id)
  try { Write-Output ('  pressedNow=' + [string]$cb.GetPressedMso($id)) } catch { Write-Output '  pressedNow=ERR' }
}
Write-Output 'done'
