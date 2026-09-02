require("dotenv").config();
const express = require("express");
const http = require("http");
const { attachRelay } = require("./relay");

const app = express();
const port = process.env.PORT || 8080;

app.get("/health", (_req, res) => {
  res.json({ status: "ok" });
});

const server = http.createServer(app);
attachRelay(server);

server.listen(port, "0.0.0.0", () => {
  console.log(`relay server listening on :${port}`);
});
