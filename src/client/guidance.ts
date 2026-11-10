import { readFile } from 'node:fs/promises';
import { z } from 'zod';

const guidanceSchema = z.object({
  version: z.string().min(1),
  rules: z.array(z.object({
    id: z.string().min(1),
    priority: z.number().int().nonnegative(),
    instruction: z.string().min(1),
  })).refine(rules => new Set(rules.map(rule => rule.id)).size === rules.length, 'Duplicate rule IDs'),
});

export const defaultGuidance = {
  version: '1',
  rules: [
    { id: 'file-documents', priority: 10, instruction: '优先将已归自己所有、暂时不用的文件放入文档柜。' },
    { id: 'store-idle-items', priority: 20, instruction: '优先将闲置自有物品放入合适的储物柜。正在交易、展示或生产使用的物品保留相应用途，缺少合适容器或容量时自行选择临时位置。' },
    { id: 'night-electricity', priority: 30, instruction: '按实际运行需求建议夜间用电。仅在计划运行的设备需要夜间外部供电时考虑订电，结合当前可见的供电、储能与消耗条件判断。信息不足时先查询设备，避免仅按持有电器作决定。' },
  ],
};

export async function loadGuidance(path = process.env.PSAI_PREFERENCES_FILE) {
  let configuration = defaultGuidance;
  let error: string | undefined;
  if (path) {
    try { configuration = guidanceSchema.parse(JSON.parse(await readFile(path, 'utf8'))); }
    catch { error = 'preferences_invalid_or_unreadable'; }
  }
  return { ...configuration, mode: 'advisory' as const, user_instructions_take_precedence: true,
    source: path && !error ? 'local' : 'default',
    rules: configuration.rules.map(rule => ({ ...rule })).sort((a, b) => a.priority - b.priority),
    ...(error ? { error } : {}),
  };
}
