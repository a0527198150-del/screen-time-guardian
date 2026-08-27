# ServiceLock - Locking the Service and Installation Folder

## What This Does

Adds an explicit Deny ACE to the service's own security descriptor and the
installation folder, preventing administrators from stopping, reconfiguring,
deleting, or uninstalling the software.

The lock opens from the control panel with the application password, and
closes automatically after 15 minutes.

## What This Stops

| Command | Result |
|---------|--------|
| `Stop-Service ScreenTimeGuardian` | Permission denied |
| `sc.exe delete ScreenTimeGuardian` | Permission denied |
| `sc.exe config ScreenTimeGuardian start= disabled` | Permission denied |
| `Remove-Item "C:\Program Files\ScreenTimeGuardian" -Recurse -Force` | Permission denied |

All four fail even in an elevated PowerShell window.

## What This Does Not Stop

- **Taking ownership (SeTakeOwnershipPrivilege):** An administrator with
  this privilege can seize the service and rewrite the descriptor. This is
  by design. The recovery path below documents exactly how.

- **Safe Mode:** The service is not registered as a running service in
  Safe Mode, so the lock does not operate at all.

- **Physical access:** Booting from external media, disk wipe, reinstall.

- **The SAFEMODE kill switch:** Continues to work by design. It is the
  safety net against boot loops. It stops enforcement - it does not
  remove the software.

## Recovery Path (Hebrew)

If the password is lost or the software is stuck, full control can be
restored from an elevated command prompt:

### Step 1 - Remove the service lock

```cmd
sc.exe sdset ScreenTimeGuardian "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)"
```

### Step 2 - Stop the service

```cmd
sc.exe stop ScreenTimeGuardian
```

### Step 3 - Remove the software

```cmd
sc.exe delete ScreenTimeGuardian
rmdir /s /q "C:\Program Files\ScreenTimeGuardian"
```

### Step 4 - Clean up registry entries (optional)

```cmd
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenTimeGuardian" /f
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v ScreenTimeGuardianAgent /f
```

### Step 5 - Remove firewall rules (optional)

```powershell
Get-NetFirewallRule -Name 'STG-*' | Remove-NetFirewallRule
```

## Acceptance Criteria (from the guide)

Before locking:
1. Save `sc.exe sdshow ScreenTimeGuardian` output from the real machine.

After Part A:
2. Open maintenance with correct password - succeeds, status shown.
3. Wrong password - rejected.
4. After 15 minutes - window closes automatically.

After Parts B and C, from elevated PowerShell (no maintenance window open):
5. All four commands (Stop, delete, config, remove) fail with permission error.

With maintenance window open:
6. Stop-Service succeeds.
7. Setup.ps1 runs full install to completion.
8. After window closes - locks restored automatically.

Recovery:
9. Running recovery path from docs restores full control. This test is mandatory.
