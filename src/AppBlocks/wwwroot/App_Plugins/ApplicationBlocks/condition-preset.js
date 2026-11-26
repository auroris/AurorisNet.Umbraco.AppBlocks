import { UmbControllerBase } from "@umbraco-cms/backoffice/class-api";
import { appBlockTypes } from "./block-types.js";

export class BlockConditionPreset extends UmbControllerBase {
    async processValue(value, config, typeArgs, args) {
        if (value !== undefined || args?.entityType !== "block" || args.alias !== "comparison") return value;
        const type = appBlockTypes.find(block => block.key === args.entityTypeUnique?.toLowerCase());
        switch (type?.alias) {
            case "appVisibilityCondition": return "Always visible";
            case "appSecurityCondition": return "Always rendered";
            case "appReadOnlyCondition": return "Never read only";
            default: return value;
        }
    }
}

export { BlockConditionPreset as api };
