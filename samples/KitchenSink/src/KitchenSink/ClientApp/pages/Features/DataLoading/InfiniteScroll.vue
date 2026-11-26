<script setup lang="ts">
import { Head, InfiniteScroll, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

import type { App } from '@/wayfinder/types';

defineProps<{
    contacts: {
        data: Array<
            Pick<
                App.Models.Contact,
                'id' | 'first_name' | 'last_name' | 'email' | 'is_favorite'
            > & {
                organization?: Pick<App.Models.Organization, 'name'> | null;
            }
        >;
    };
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Data Loading' },
    { title: 'Infinite Scroll' },
];

const mode = ref<'auto' | 'manual' | 'manual-after' | 'reset'>('auto');
const infiniteScrollRef = ref<InstanceType<typeof InfiniteScroll> | null>(null);
const favoritesOnly = ref(false);

function applyFilter() {
    router.reload({
        data: { favorites: favoritesOnly.value ? '1' : '0' },
        only: ['contacts'],
        reset: ['contacts'],
    });
}
</script>

<template>
    <Head title="Infinite Scroll" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Infinite Scroll"
                docs="data-props/infinite-scroll"
                controller="app/Http/Controllers/Feature/DataLoadingController.php#L56"
            >
                Auto, manual, and manual-after modes using
                <code class="text-xs">Inertia::scroll()</code> with pagination.
            </FeatureHeader>

            <!-- Mode selector -->
            <FeatureCard
                title="Scroll Mode"
                description="Switch between InfiniteScroll modes. The list below updates accordingly."
            >
                <div class="flex flex-wrap gap-2">
                    <Button
                        :variant="mode === 'auto' ? 'default' : 'outline'"
                        size="sm"
                        @click="mode = 'auto'"
                    >
                        Auto (default)
                    </Button>
                    <Button
                        :variant="mode === 'manual' ? 'default' : 'outline'"
                        size="sm"
                        @click="mode = 'manual'"
                    >
                        Manual
                    </Button>
                    <Button
                        :variant="
                            mode === 'manual-after' ? 'default' : 'outline'
                        "
                        size="sm"
                        @click="mode = 'manual-after'"
                    >
                        Manual After 2 Pages
                    </Button>
                    <Button
                        :variant="mode === 'reset' ? 'default' : 'outline'"
                        size="sm"
                        @click="mode = 'reset'"
                    >
                        Reset on Filter
                    </Button>
                </div>
            </FeatureCard>

            <!-- Auto mode -->
            <FeatureCard
                v-if="mode === 'auto'"
                title="Auto Mode"
                description="Loads more items automatically as you scroll near the bottom. The buffer (200px) triggers loading before you reach the end for a smoother experience."
            >
                <InfiniteScroll
                    data="contacts"
                    class="space-y-2"
                    :buffer="200"
                    preserve-url
                >
                    <div
                        v-for="contact in contacts.data"
                        :key="contact.id"
                        class="flex items-center justify-between rounded border border-black/10 px-3 py-2 dark:border-white/10"
                    >
                        <div>
                            <p class="text-sm font-medium">
                                {{ contact.first_name }} {{ contact.last_name }}
                            </p>
                            <p class="text-xs text-muted-foreground">
                                {{ contact.email }}
                            </p>
                        </div>
                        <div class="flex items-center gap-2">
                            <Badge
                                v-if="contact.organization"
                                variant="outline"
                                class="text-xs"
                                >{{ contact.organization.name }}</Badge
                            >
                            <span
                                v-if="contact.is_favorite"
                                class="text-yellow-500"
                                >&#9733;</span
                            >
                        </div>
                    </div>

                    <template #loading>
                        <div
                            class="py-3 text-center text-sm text-muted-foreground"
                        >
                            Loading more contacts...
                        </div>
                    </template>
                </InfiniteScroll>
            </FeatureCard>

            <!-- Manual mode -->
            <FeatureCard
                v-if="mode === 'manual'"
                title="Manual Mode"
                description="Click the button to load more items. No automatic loading."
            >
                <InfiniteScroll
                    ref="infiniteScrollRef"
                    data="contacts"
                    class="space-y-2"
                    manual
                    preserve-url
                >
                    <div
                        v-for="contact in contacts.data"
                        :key="contact.id"
                        class="flex items-center justify-between rounded border border-black/10 px-3 py-2 dark:border-white/10"
                    >
                        <div>
                            <p class="text-sm font-medium">
                                {{ contact.first_name }} {{ contact.last_name }}
                            </p>
                            <p class="text-xs text-muted-foreground">
                                {{ contact.email }}
                            </p>
                        </div>
                        <div class="flex items-center gap-2">
                            <Badge
                                v-if="contact.organization"
                                variant="outline"
                                class="text-xs"
                                >{{ contact.organization.name }}</Badge
                            >
                            <span
                                v-if="contact.is_favorite"
                                class="text-yellow-500"
                                >&#9733;</span
                            >
                        </div>
                    </div>

                    <template #next="{ loading, fetch, hasMore }">
                        <div v-if="hasMore" class="pt-3 text-center">
                            <Button
                                variant="outline"
                                :disabled="loading"
                                @click="fetch"
                            >
                                {{ loading ? 'Loading...' : 'Load More' }}
                            </Button>
                        </div>
                        <p
                            v-else
                            class="pt-3 text-center text-sm text-muted-foreground"
                        >
                            No more contacts.
                        </p>
                    </template>
                </InfiniteScroll>
            </FeatureCard>

            <!-- Manual-after mode -->
            <FeatureCard
                v-if="mode === 'manual-after'"
                title="Manual After 2 Pages"
                description="Auto-loads for the first 2 pages, then switches to a manual button."
            >
                <InfiniteScroll
                    data="contacts"
                    class="space-y-2"
                    :manual-after="2"
                    preserve-url
                >
                    <div
                        v-for="contact in contacts.data"
                        :key="contact.id"
                        class="flex items-center justify-between rounded border border-black/10 px-3 py-2 dark:border-white/10"
                    >
                        <div>
                            <p class="text-sm font-medium">
                                {{ contact.first_name }} {{ contact.last_name }}
                            </p>
                            <p class="text-xs text-muted-foreground">
                                {{ contact.email }}
                            </p>
                        </div>
                        <div class="flex items-center gap-2">
                            <Badge
                                v-if="contact.organization"
                                variant="outline"
                                class="text-xs"
                                >{{ contact.organization.name }}</Badge
                            >
                            <span
                                v-if="contact.is_favorite"
                                class="text-yellow-500"
                                >&#9733;</span
                            >
                        </div>
                    </div>

                    <template #loading>
                        <div
                            class="py-3 text-center text-sm text-muted-foreground"
                        >
                            Loading more contacts...
                        </div>
                    </template>

                    <template #next="{ loading, fetch, hasMore, manualMode }">
                        <div
                            v-if="manualMode && hasMore"
                            class="pt-3 text-center"
                        >
                            <Button
                                variant="outline"
                                :disabled="loading"
                                @click="fetch"
                            >
                                {{ loading ? 'Loading...' : 'Load More' }}
                            </Button>
                        </div>
                    </template>
                </InfiniteScroll>
            </FeatureCard>

            <!-- Reset mode -->
            <FeatureCard v-if="mode === 'reset'" title="Reset on Filter Change">
                <template #description>
                    Use <code class="text-xs">reset: ['contacts']</code> to
                    clear loaded pages when filters change.
                </template>
                <div
                    class="mb-4 flex items-center gap-2 rounded-md bg-muted p-3"
                >
                    <input
                        id="favorites-filter"
                        type="checkbox"
                        v-model="favoritesOnly"
                        @change="applyFilter"
                        class="size-4 rounded border"
                    />
                    <label for="favorites-filter" class="text-sm">
                        Show favorites only
                    </label>
                </div>

                <InfiniteScroll data="contacts" class="space-y-2" preserve-url>
                    <div
                        v-for="contact in contacts.data"
                        :key="contact.id"
                        class="flex items-center justify-between rounded border border-black/10 px-3 py-2 dark:border-white/10"
                    >
                        <div>
                            <p class="text-sm font-medium">
                                {{ contact.first_name }} {{ contact.last_name }}
                            </p>
                            <p class="text-xs text-muted-foreground">
                                {{ contact.email }}
                            </p>
                        </div>
                        <div class="flex items-center gap-2">
                            <Badge
                                v-if="contact.organization"
                                variant="outline"
                                class="text-xs"
                                >{{ contact.organization.name }}</Badge
                            >
                            <span
                                v-if="contact.is_favorite"
                                class="text-yellow-500"
                                >&#9733;</span
                            >
                        </div>
                    </div>

                    <template #loading>
                        <div
                            class="py-3 text-center text-sm text-muted-foreground"
                        >
                            Loading more contacts...
                        </div>
                    </template>
                </InfiniteScroll>

                <div class="mt-4 space-y-2">
                    <CodeBlock
                        title="Code"
                        code="router.reload({
  data: { favorites: '1' },
  only: ['contacts'],
  reset: ['contacts'], // Clears loaded pages
})"
                    />
                    <p class="text-sm text-muted-foreground">
                        Without <code>reset</code>, new filtered results would
                        merge with stale pages.
                    </p>
                </div>
            </FeatureCard>
        </div>
    </AppLayout>
</template>
