# Guardian Updater

`Guardian.Updater` is the privileged, signed-package installation helper used by the service update coordinator. It is not dead code: `UpdateCoordinator` launches `ScreenTimeGuardian.Updater.exe` only after validating the HTTPS manifest, SHA-256 digest, and RSA signature.

The updater receives the package path, installation directory, service name, expected hash, signature, public key, version, and package URL as command-line arguments. It validates archive paths, creates a backup, stops the service, installs the package, verifies that the service starts, and restores the backup if installation fails.

The updater must remain included in release packages because an update that is downloaded and verified cannot be applied without it. It does not change DNS, hosts, firewall policy, or browser extension files directly; it only replaces the signed application package and controls the service lifecycle during the replacement.
