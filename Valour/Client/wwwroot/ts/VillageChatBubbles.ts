export const VILLAGE_BUBBLE_HOLD_MS = 7000;
export const VILLAGE_BUBBLE_FADE_MS = 1200;
export const VILLAGE_BUBBLE_STACK_LIMIT = 4;

export type VillageChatBubble = {
    text: string;
    bornAt: number;
    messageId?: string;
};

/** Adds each confirmed message once, regardless of HTTP and realtime arrival order. */
export function enqueueVillageBubble(
    bubbles: Map<string, VillageChatBubble[]>,
    userId: string | number,
    text: string,
    now = performance.now(),
    messageId?: string
): VillageChatBubble[] {
    const key = String(userId);
    const safeText = String(text);
    const queue = bubbles.get(key) ?? [];
    if (messageId && queue.some(bubble => bubble.messageId === messageId)) {
        return queue;
    }

    queue.push({ text: safeText, bornAt: now, messageId });
    if (queue.length > VILLAGE_BUBBLE_STACK_LIMIT) {
        queue.splice(0, queue.length - VILLAGE_BUBBLE_STACK_LIMIT);
    }

    bubbles.set(key, queue);
    return queue;
}

export function getVillageBubbleAlpha(
    age: number,
    holdMs = VILLAGE_BUBBLE_HOLD_MS,
    fadeMs = VILLAGE_BUBBLE_FADE_MS
): number {
    if (age <= holdMs) {
        return 1;
    }

    return Math.max(0, 1 - ((age - holdMs) / fadeMs));
}
