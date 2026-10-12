# AWS for Cable — what you have, what to ignore, what to set up

> AWS has 200+ services. Cable uses **one**: a **Lightsail** server in Stockholm that runs the charger
> server (`ocpp.cable-app.com`). Everything else (API, database, admin and partner portals, `ocpp-dev`)
> is at SmarterASP. This guide is only about that one server and the AWS account around it.
> Menu names in the AWS console change a little from time to time; if a label differs, look for the
> nearest match.

---

## 1. What you have on AWS

| Item | Value |
|---|---|
| AWS account | "Cable" |
| Service | **Amazon Lightsail** (the simple, fixed-price corner of AWS) — open it at **lightsail.aws.amazon.com** |
| Region | **Stockholm (eu-north-1)**. In the AWS console, the region picker (top right) must say Stockholm or you will see an empty account. |
| Server (instance) | **Cable-OCPP** — Windows Server 2025, 2 vCPU, 2 GB RAM, 60 GB SSD, 3 TB transfer / month |
| Price | **$22 / month**, fixed. Extra only for snapshots (a few cents per GB) and transfer above 3 TB (you are far below). |
| Public IP | **13.61.45.26** (static) → DNS `ocpp.cable-app.com` (the A record is in the SmarterASP DNS panel, not in AWS) |
| What runs on it | IIS site `ocpp` (folder `C:\inetpub\ocpp`, app pool `ocpp`, Always Running) = `Cable.Ocpp` |
| HTTPS certificate | Let's Encrypt via **win-acme**, renews itself (needs port 80 open) |
| Open ports | **443** chargers (wss) and HTTPS · **80** certificate renewal · **8172** Web Deploy from Visual Studio (only your IPs) · **3389** remote desktop |
| Deploy | Visual Studio → `aws-ocpp - Web Deploy` profile, user `Administrator`, password from Lightsail |
| Health | `https://ocpp.cable-app.com/health` → 200. `/status` (with the `X-Api-Key` header) lists connected chargers |
| **Database it uses today** | **the dev database** (environment *Staging*). It becomes the production charger server only at the production switch (go-live checklist §2). |

How the pieces fit:

```
Charger (RH4) ──wss:443──► ocpp.cable-app.com  (AWS Lightsail, Stockholm)
                                   │ SQL
                                   ▼
                          SQL Server at SmarterASP (Amsterdam)  ◄── Cable API / admin / partner (SmarterASP)
```

---

## 2. Words you will see, and what you can ignore

| Term | Meaning for you |
|---|---|
| **Region** | Where the server physically is. Yours: Stockholm. Always check the region picker first. |
| **Lightsail** | Simple servers at a fixed monthly price. **This is where your server lives.** |
| **EC2** | The "full" server service. Lightsail is built on it — you will see the name `EC2AMAZ-…` as the Windows machine name. You do **not** need to open EC2. |
| **Instance** | A server. |
| **Static IP** | A public IP that stays the same across restarts. |
| **Snapshot** | A full copy of the server's disk. Restoring one gives you the server as it was. |
| **Firewall (Networking tab)** | Which ports are open, and to whom. |
| **Root user** | The email that created the account. Can do everything, including close the account. Use it rarely. |
| **IAM user** | A normal login inside the account, with only the rights you give it. Use this daily. |
| **MFA** | A second factor (code from an authenticator app) on top of the password. |
| **Billing / Budgets** | Where the bill is and where you set a spending alert. |

**Ignore for now:** EC2, S3, RDS, Lambda, ECS / EKS / containers, CloudFront, Route 53, VPC, Amplify, Bedrock, SageMaker,
and everything else in the services list. None of them is used by Cable.

---

## 3. Step by step — one-time setup (≈ 45 minutes)

Do them in this order. Each step is safe: none of them stops the server.

### Step 1 — Sign in

1. aws.amazon.com → **Sign in to console**.
2. Choose **Root user**, enter the account email and password (the first time; after step 4 you will use the IAM user).

### Step 2 — Spending alert (protects you from a surprise bill)

1. Search bar at the top → type **Budgets** → open **Billing and Cost Management → Budgets**.
2. **Create budget** → **Use a template** → **Monthly cost budget**.
3. Name `Cable monthly`, amount **$30** (the server is $22; anything above $30 means something unexpected is running).
4. Email: yours (and one teammate). → **Create budget**.

You get an email when spending passes 85 % and 100 % of $30.

### Step 3 — MFA on the root user

1. Top right, click the account name → **Security credentials**.
2. **Multi-factor authentication (MFA)** → **Assign MFA device** → name `root-phone` → **Authenticator app**.
3. Scan the QR code with Google Authenticator / Microsoft Authenticator, enter two codes in a row → **Add MFA**.
4. Save the account email, password and the fact that MFA is on in the company password manager.

### Step 4 — An everyday login (IAM user)

1. Search **IAM** → **Users** → **Create user**.
2. Name e.g. `anas`, tick **Provide user access to the AWS Management Console**, choose **I want to create an IAM user**, set a password.
3. Permissions: **Attach policies directly** → for a small team, `AdministratorAccess` is the simple choice
   (or only `AmazonLightsailFullAccess` + `AWSBillingReadOnlyAccess` if this login should never touch anything else).
4. Create, then open the user → **Security credentials** → **Assign MFA device** (same as step 3).
5. Note the **sign-in URL** shown on the IAM dashboard (`https://<account-id>.signin.aws.amazon.com/console`) — that is where this user logs in.
6. From now on use this user; keep the root user for billing and emergencies.

### Step 5 — Check the static IP

1. Open **lightsail.aws.amazon.com** (region Stockholm) → **Networking** (left menu).
2. You should see a static IP **13.61.45.26** attached to **Cable-OCPP**. If it is listed: nothing to do.
3. ⚠️ If there is **no** static IP: do **not** create one now without planning — attaching a new static IP gives the server a **new** address,
   and `ocpp.cable-app.com` (SmarterASP DNS) must be updated to it, otherwise chargers cannot connect. Ask the backend before doing it.

### Step 6 — Firewall: open only what is needed

Lightsail → **Instances** → **Cable-OCPP** → **Networking** tab → **IPv4 Firewall**. The rules should be:

| Application | Port | Allowed from |
|---|---|---|
| HTTPS | 443 | **Any IPv4** (chargers connect from anywhere) |
| HTTP | 80 | **Any IPv4** (certificate renewal) |
| Custom TCP | 8172 | **Restrict to IP addresses**: `94.128.0.0/15` (home range) and `188.236.0.0/16` (office range) |
| RDP | 3389 | **Restrict to IP addresses** (same two ranges) **and tick "Allow Lightsail browser RDP"** |

The ranges stop the "Could not connect to the remote computer" error every time your home / office IP changes,
while still keeping the deploy and remote-desktop ports closed to the internet. Remote desktop through the Lightsail
web page keeps working thanks to the tick box.

Do the same under **IPv6 Firewall** if it is enabled: 443 and 80 open, nothing else.

### Step 7 — Backups (snapshots)

1. **Cable-OCPP** → **Snapshots** tab.
2. **Automatic snapshots** → switch **On**, time **00:00 UTC** (03:00 Jordan, when nobody charges). Lightsail keeps the last 7 days.
3. If the automatic option is not offered for this Windows server, use **Create snapshot** manually — at least **before every deploy** and once a week.
   Name it with the date, e.g. `cable-ocpp-2026-10-12`.

To restore: Snapshots tab → the snapshot → **Create new instance**; then move the static IP to the new instance (Networking).
The DNS name stays the same because the IP moves with it.

### Step 8 — Alarms by email

1. Lightsail → **Account** (top right) → **Account** → **Notifications** / **Contact information** → add your **email** and confirm the email AWS sends.
2. **Cable-OCPP** → **Metrics** tab:
   - Metric **Status check failures** → **Add alarm** → "greater than or equal to **1**" for **2** of the last **2** data points → notify by email. *(Server or hardware not responding.)*
   - Metric **CPU utilization** → **Add alarm** → "greater than **80 %**" for **3** of the last **3** data points → email. *(Server overloaded.)*

### Step 9 — Watch the application itself (outside AWS)

AWS alarms tell you the **machine** is down, not that **the charger server inside it** stopped. Add a free check on the URL:

1. uptimerobot.com → free account → **Add New Monitor** → type **HTTP(s)**, URL `https://ocpp.cable-app.com/health`, interval 5 min.
2. Alert contact: your email (and the phone app if you want push).

---

## 4. Routine

| When | What |
|---|---|
| Before every deploy to AWS | Manual snapshot (step 7.3) if automatic snapshots are off |
| After every deploy | Open `https://ocpp.cable-app.com/health` → 200 |
| "Could not connect" in Visual Studio | Your IP is outside the ranges in step 6 → add it (Networking → IPv4 firewall → 8172) |
| Monthly | Billing → Bills: should be ≈ $22 + cents. Anything else → find and stop it. |
| An alarm email | Lightsail → Cable-OCPP → **Reboot** if the status check failed; check `/health` after 2 minutes |

## 5. When to change something

| Situation | Action |
|---|---|
| Many chargers / CPU alarm often | Snapshot → create a new instance from it on the **$44** plan (4 GB), move the static IP. ~15 min, no code change. |
| Production go-live | No AWS change — the switch is configuration on the server (connection string + environment), see the go-live checklist §2. |
| Someone leaves the team | IAM → Users → delete their user. Change the server's Administrator password (Lightsail → instance → Connect → RDP → Windows). |

---

## 6. What not to do

- Don't **stop** or **delete** the instance to "save money" — chargers disconnect immediately.
- Don't open **8172** or **3389** to *Any IPv4*.
- Don't create resources in other regions or other services "to try" — they bill separately and are easy to forget.
- Don't share the root password; give people IAM users.
