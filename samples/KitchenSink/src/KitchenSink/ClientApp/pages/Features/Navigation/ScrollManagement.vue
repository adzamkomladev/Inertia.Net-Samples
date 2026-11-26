<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    timestamp: string;
    items: Array<{ id: number; title: string; description: string }>;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'Scroll Management' },
];
</script>

<template>
    <Head title="Scroll Management" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Scroll Management"
                docs="advanced/scroll-management"
                controller="app/Http/Controllers/Feature/NavigationController.php#L135"
            >
                Inertia resets scroll position to the top on every page visit
                and restores it during back/forward navigation.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <FeatureCard
                    title="Scroll Behavior"
                    description="By default, scroll resets to the top on navigation. Use preserveScroll to keep the current position."
                >
                    <div class="space-y-4">
                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">
                                Default (resets scroll)
                            </h4>
                            <div class="flex items-center gap-2">
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.visit(
                                            '/features/navigation/scroll-management',
                                        )
                                    "
                                >
                                    router.visit()
                                </Button>
                                <Link
                                    href="/features/navigation/scroll-management"
                                    class="text-sm text-primary underline underline-offset-2"
                                >
                                    &lt;Link&gt;
                                </Link>
                            </div>
                        </div>

                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">
                                preserveScroll: true
                            </h4>
                            <div class="flex items-center gap-2">
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.visit(
                                            '/features/navigation/scroll-management',
                                            {
                                                preserveScroll: true,
                                            },
                                        )
                                    "
                                >
                                    router.visit()
                                </Button>
                                <Link
                                    href="/features/navigation/scroll-management"
                                    preserve-scroll
                                    class="text-sm text-primary underline underline-offset-2"
                                >
                                    &lt;Link preserve-scroll&gt;
                                </Link>
                            </div>
                        </div>

                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">
                                preserveScroll: callback
                            </h4>
                            <p class="text-xs text-muted-foreground">
                                Dynamically decide based on the response.
                            </p>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="
                                    router.visit(
                                        '/features/navigation/scroll-management',
                                        {
                                            preserveScroll: (page) =>
                                                page.props.timestamp !== null,
                                        },
                                    )
                                "
                            >
                                preserveScroll: callback
                            </Button>
                        </div>
                    </div>
                </FeatureCard>

                <FeatureCard
                    title="Scroll Regions"
                    description="Track scroll position for elements with overflow, not just the document."
                >
                    <div class="space-y-3">
                        <p class="text-xs text-muted-foreground">
                            Add the
                            <code>scroll-region</code> attribute to any
                            scrollable container. Inertia will track and restore
                            its scroll position during back/forward navigation.
                        </p>
                        <div
                            scroll-region
                            class="h-48 space-y-2 overflow-y-auto rounded-lg border border-black/10 p-3 dark:border-white/10"
                        >
                            <div
                                v-for="item in items"
                                :key="item.id"
                                class="flex items-center justify-between rounded-lg bg-muted/30 px-3 py-2"
                            >
                                <div>
                                    <span class="text-sm font-medium">{{
                                        item.title
                                    }}</span>
                                    <p class="text-xs text-muted-foreground">
                                        {{ item.description }}
                                    </p>
                                </div>
                                <Badge variant="secondary" class="text-xs">
                                    #{{ item.id }}
                                </Badge>
                            </div>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Scroll down in the list above, navigate away, then
                            use the browser back button. The scroll position
                            will be restored.
                        </p>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
