<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    timestamp: string;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'Preserve Scroll' },
];

const items = Array.from({ length: 30 }, (_, i) => ({
    id: i + 1,
    title: `Item ${i + 1}`,
    description: `This is item number ${i + 1}. Scroll down to see more items and test scroll preservation.`,
}));
</script>

<template>
    <Head title="Preserve Scroll" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Preserve Scroll"
                docs="advanced/scroll-management#scroll-preservation"
                controller="app/Http/Controllers/Feature/NavigationController.php#L33"
            >
                Scroll position management. Scroll down, then click a reload
                button to see the difference.
            </FeatureHeader>

            <!-- Controls -->
            <FeatureCard
                title="Reload Options"
                description="Scroll down the list below, then click a button to reload. Compare scroll behavior."
            >
                <div class="flex flex-wrap gap-3">
                    <div class="space-y-1">
                        <Button
                            variant="destructive"
                            @click="
                                router.visit(
                                    '/features/navigation/preserve-scroll',
                                )
                            "
                        >
                            Default (scroll to top)
                        </Button>
                        <p class="text-xs text-muted-foreground">
                            Page scrolls back to top.
                        </p>
                    </div>
                    <div class="space-y-1">
                        <Button
                            @click="
                                router.visit(
                                    '/features/navigation/preserve-scroll',
                                    { preserveScroll: true },
                                )
                            "
                        >
                            preserveScroll: true
                        </Button>
                        <p class="text-xs text-muted-foreground">
                            Scroll position maintained.
                        </p>
                    </div>
                    <div class="space-y-1">
                        <Button variant="secondary" @click="router.reload()">
                            router.reload()
                        </Button>
                        <p class="text-xs text-muted-foreground">
                            Reload preserves scroll by default.
                        </p>
                    </div>
                </div>
                <div class="mt-3 flex items-center gap-2">
                    <span class="text-sm text-muted-foreground"
                        >Server timestamp:</span
                    >
                    <Badge variant="outline">{{ timestamp }}</Badge>
                </div>
            </FeatureCard>

            <!-- Scroll Region Demo -->
            <FeatureCard title="Scroll Region">
                <template #description>
                    This container has
                    <code class="rounded bg-muted px-1 py-0.5 text-xs"
                        >scroll-region</code
                    >. Inertia tracks its scroll position separately.
                </template>
                <div
                    class="h-64 overflow-y-auto rounded-md border border-black/10 dark:border-white/10"
                    scroll-region
                >
                    <div class="space-y-2 p-3">
                        <div
                            v-for="item in items"
                            :key="item.id"
                            class="rounded-md border border-black/10 p-3 dark:border-white/10"
                        >
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium">{{
                                    item.title
                                }}</span>
                                <Badge variant="secondary" class="text-xs"
                                    >#{{ item.id }}</Badge
                                >
                            </div>
                            <p class="mt-1 text-xs text-muted-foreground">
                                {{ item.description }}
                            </p>
                        </div>
                    </div>
                </div>
            </FeatureCard>

            <!-- Long list to demonstrate page-level scrolling -->
            <div class="space-y-2">
                <div
                    v-for="item in items"
                    :key="'page-' + item.id"
                    class="rounded-xl bg-muted/30 p-4"
                >
                    <div class="flex items-center justify-between">
                        <span class="font-medium">{{ item.title }}</span>
                        <Badge variant="outline">#{{ item.id }}</Badge>
                    </div>
                    <p class="mt-1 text-sm text-muted-foreground">
                        {{ item.description }}
                    </p>
                </div>
            </div>
        </div>
    </AppLayout>
</template>
