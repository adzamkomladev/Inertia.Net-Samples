import { route } from '../../../../core';

export const show = route('get', '/features/data-loading/live-props');
export const castVote = route('post', '/features/data-loading/live-props/votes');
export const addReaction = route('post', '/features/data-loading/live-props/reactions');
export const burstTicks = route('post', '/features/data-loading/live-props/ticks');
