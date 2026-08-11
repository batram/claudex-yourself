for (let attempt = 1; attempt <= 5; attempt++) {
  await claudex.request("config/mcpServer/reload");
  const status = await claudex.request("mcpServerStatus/list", { detail: "toolsAndAuthOnly" });
  const self = status?.data?.find(server => server.name === "claudex-yourself");
  const toolCount = self?.tools ? Object.keys(self.tools).length : 0;
  if (self?.serverInfo && toolCount > 0) return { attempt, toolCount, version: self.serverInfo.version };
  if (attempt < 5) await claudex.sleep(750);
}
throw new Error("claudex-yourself did not reconnect after five completed reload attempts");
