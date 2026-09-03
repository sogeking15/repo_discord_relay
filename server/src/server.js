require("dotenv").config();
const express = require("express");
const http = require("http");
const { attachRelay } = require("./relay");

const app = express();
const port = process.env.PORT || 8080;

app.get("/health", (_req, res) => {
  res.json({ status: "ok" });
});

app.post("/shutdown", (req, res) => {
  const ip = req.socket.remoteAddress;
  if (ip !== "127.0.0.1" && ip !== "::1" && ip !== "::ffff:127.0.0.1") {
    return res.status(403).json({ error: "forbidden" });
  }
  res.json({ status: "shutting down" });
  setTimeout(() => process.exit(0), 100);
});

const server = http.createServer(app);
attachRelay(server);

server.listen(port, "0.0.0.0", () => {
  console.log(`relay server listening on :${port}`);
});
