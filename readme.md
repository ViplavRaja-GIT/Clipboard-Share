# 🚀 Clipboard Sharing (a.k.a copy-paste on steroids)
Why send emails to yourself or drag files around like it’s 2005?
This beast lets you share your clipboard across machines over the internet — text, images, files… all of it.
Think of it as AirDrop, but you control it, and it doesn’t cry about ecosystems.

## ⚡ Status
- ✅ Its working fine as expected - **Done**
- 🔄 Code could use a shower (cleanup). - **In Progress**

## 🛠️ What You’ll Need
  - ### Node JS (the brain)
  - ### dotnet (the muscle)

## 🏗️ Setup Like a Pro
  - ### Clone it:
    ```
    git clone https://@github.com/ViplavRaja-GIT/Clipboard-Share.git
    cd Clipboard-Share
    npm install
    ```

  - ### Build the helper:
    ```
    cd Clipboard-Share/helper
    dotnet restore
    dotnet build
    ```

## 🎮 How to Play
On PC-A run command
```
node clip-sync-help.js --port 3030 --peers <PC-B ip address>:4040 --key shared123 --helper "<helper exe path>"
```

On PC-B run command
```
node clip-sync-help.js --port 4040 --peers <PC-A ip address>:3030 --key shared123 --helper "<helper exe path>"
```
Boom. Your clipboards are now soulmates.


## 🧠 How It Works (Quick & Dirty)
- Each machine spins up a Socket.IO server on its port.
- Each also moonlights as a client, connected to peers.
- Copy something → it gets hashed, broadcast, injected into the hive.
- Incoming clipboards get validated, written locally, and echoed to peers.
- Hashes prevent endless loops (no infinite copy-paste nightmares).
- Same --key keeps the link private.
- Open your firewall, or it stays in the shadows.

## 👥 Collaborators
[![ViplavRaja-GIT](https://github.com/ViplavRaja-GIT.png?size=10)](https://github.com/ViplavRaja-GIT)
[![HarshitRaja1999](https://github.com/HarshitRaja1999.png?size=10)](https://github.com/HarshitRaja1999)

🔥 Copy something on one machine, it magically appears on the other. No BS. No begging Big Tech for permission.
