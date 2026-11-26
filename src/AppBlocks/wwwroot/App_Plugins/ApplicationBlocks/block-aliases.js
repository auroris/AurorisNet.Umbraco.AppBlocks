import { nestedBlockAliases } from "./block-preview.js";

export function onInit(_host, registry) {
    const aliases = registry?.getByAlias("AppBlocks.BlockTypeAliases")?.meta?.aliases;
    if (!aliases || typeof aliases !== "object") return;
    for (const [key, alias] of Object.entries(aliases)) {
        if (typeof alias === "string" && alias.length > 0) nestedBlockAliases.set(key.toLowerCase(), alias);
    }
}
