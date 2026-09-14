# JOVANISMOF 5.1.3

Gold earned by any player is credited equally to everyone, with separate wallets.
Spending only reduces the buyer's balance. Earnings now use the amount actually
credited by the game, exclude remote inventory mirrors, and avoid applying the
earning cap again on receiving peers. Gold sharing also works with shared XP off.

Chest affordability uses the displayed integer balance, including exact-price
purchases. Shared chest interactions allow a non-buyer with no gold to receive
the shared reward without buying the chest again.

Host scaling controls move in five percentage point increments, allowing exactly
100% extra mob or boss health. Character menu component lookup and start-lock
retry handling were corrected to address lobby exceptions and duplicate starts.
Installer verification preserves existing desktop shortcuts.

Validation details are supplied in VALIDATION.txt with the installer.

Known limits: this release does not complete mid-run rejoining, which is still
blocked by the public matchmaking service. World checkpoints do not yet restore
every transient projectile, attack phase, or effect queue. Long sessions and
China-to-South-Africa connectivity have not been verified in this release.

Existing installations configured for to31109-ctrl/jovanismof check for the update
at launch, download it, and apply it after the game closes. Relaunch afterwards.
All players should update before starting a session. New players should download
the installer archive, extract it, and run Install.cmd.
