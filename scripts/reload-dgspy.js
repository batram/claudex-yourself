for (let attempt = 1; attempt <= 3; attempt++) {
  await claudex.request("config/mcpServer/reload");
  const status = await claudex.request("mcpServerStatus/list", { detail: "toolsAndAuthOnly" });
  const dgspy = status?.data?.find(server => server.name === "dgspy");
  const toolCount = dgspy?.tools ? Object.keys(dgspy.tools).length : 0;
  if (dgspy?.serverInfo && toolCount > 0) {
    return { attempt, toolCount, version: dgspy.serverInfo.version };
  }
  if (attempt < 3) await claudex.sleep(750);
}
throw new Error("dgSpy did not reconnect after three completed reload attempts");
