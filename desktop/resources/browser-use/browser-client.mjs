export async function setupAtlasRuntime(options = {}) {
  const globals = options.globals ?? globalThis
  const setup = globals.__dotcraftSetupAtlasRuntime ?? globalThis.__dotcraftSetupAtlasRuntime
  if (typeof setup !== 'function') {
    throw new Error('DotCraft IAB runtime is not available in this Node REPL context.')
  }
  return setup({ ...options, globals })
}
