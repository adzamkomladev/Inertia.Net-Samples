<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Events & Lifecycle' },
    { title: 'Progress Bar' },
];
</script>

<template>
    <Head title="Progress Bar" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Progress Bar"
                docs="advanced/progress-indicators"
                controller="app/Http/Controllers/Feature/EventController.php#L32"
            >
                Built-in progress indicator with configurable delay, color, and
                spinner.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <FeatureCard
                    title="Trigger Progress"
                    description="The progress bar appears after a configurable delay (default 250ms)."
                >
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button
                                size="sm"
                                @click="
                                    router.get('/features/events/progress/slow')
                                "
                            >
                                Slow Request (2s)
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="router.reload()"
                            >
                                Normal Reload
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="router.reload({ showProgress: false })"
                            >
                                No Progress Bar
                            </Button>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            The slow request takes 2 seconds. Watch the progress
                            bar at the top of the page.
                        </p>
                    </div>
                </FeatureCard>

                <FeatureCard info-card title="Configuration">
                    <template #description>
                        Set in <code class="text-xs">createInertiaApp</code>.
                    </template>
                    <CodeBlock
                        code="
                        createInertiaApp({
                          progress: {
                            delay: 250,        // ms before bar shows
                            color: '#29d',     // bar color
                            includeCSS: true,  // include NProgress styles
                            showSpinner: false, // show spinner icon
                          },
                        })
                    "
                    />
                </FeatureCard>

                <FeatureCard
                    info-card
                    title="Per-Visit Control"
                    description="Disable progress bar for specific visits."
                >
                    <div class="space-y-3">
                        <CodeBlock
                            title="Disable per visit:"
                            code="
                                router.get('/url', {}, {
                                  showProgress: false,
                                })
                            "
                        />
                        <CodeBlock
                            title="Async visits:"
                            code="
                                // Async requests hide progress by default
                                router.get('/url', {}, {
                                  async: true,
                                  showProgress: true, // opt-in
                                })
                            "
                        />
                    </div>
                </FeatureCard>

                <FeatureCard info-card title="How It Works">
                    <div class="space-y-3">
                        <div class="flex items-center gap-3">
                            <Badge variant="outline" class="w-16 justify-center"
                                >1</Badge
                            >
                            <span class="text-sm"
                                >Visit starts. Delay timer begins (default
                                250ms)</span
                            >
                        </div>
                        <div class="flex items-center gap-3">
                            <Badge variant="outline" class="w-16 justify-center"
                                >2</Badge
                            >
                            <span class="text-sm"
                                >If still loading after delay. Progress bar
                                appears</span
                            >
                        </div>
                        <div class="flex items-center gap-3">
                            <Badge variant="outline" class="w-16 justify-center"
                                >3</Badge
                            >
                            <span class="text-sm"
                                >Bar trickles forward automatically (easing
                                animation)</span
                            >
                        </div>
                        <div class="flex items-center gap-3">
                            <Badge variant="outline" class="w-16 justify-center"
                                >4</Badge
                            >
                            <span class="text-sm"
                                >Visit completes. Bar fills to 100% and fades
                                out</span
                            >
                        </div>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
