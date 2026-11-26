<script setup lang="ts">
import { Head } from '@inertiajs/vue3';
import { ref } from 'vue';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import PersistentDemoLayout from '@/layouts/PersistentDemoLayout.vue';

defineOptions({ layout: PersistentDemoLayout });

const pageCounter = ref(0);
</script>

<template>
    <Head title="Persistent Layouts" />

    <div class="grid gap-6 lg:grid-cols-2">
        <FeatureCard
            title="Page 1 State"
            description="This state belongs to the page component. It resets every time you navigate."
        >
            <div class="space-y-4">
                <div class="flex items-center gap-4">
                    <span class="text-sm font-medium">Page counter:</span>
                    <Button
                        variant="outline"
                        size="sm"
                        class="size-7 p-0"
                        @click="pageCounter--"
                    >
                        -
                    </Button>
                    <Badge variant="secondary" class="font-mono text-base">
                        {{ pageCounter }}
                    </Badge>
                    <Button
                        variant="outline"
                        size="sm"
                        class="size-7 p-0"
                        @click="pageCounter++"
                    >
                        +
                    </Button>
                </div>

                <p class="text-xs text-muted-foreground">
                    Increment this counter, switch to Page 2, then come back.
                    This resets to 0 because the page component remounts. The
                    stopwatch and counter above persist because they live in the
                    layout.
                </p>
            </div>
        </FeatureCard>

        <FeatureCard
            info-card
            title="How It Works"
            description="Pages declare their layout with defineOptions instead of wrapping content in the template."
        >
            <div class="space-y-3">
                <CodeBlock
                    title="Persistent (this page):"
                    code="defineOptions({
  layout: PersistentDemoLayout,
})

// No layout wrapper in template"
                />
                <CodeBlock title="Standard (non-persistent):">
                    <textarea>
<AppLayout :breadcrumbs="...">
  <!-- page content -->
</AppLayout>
                    </textarea>
                </CodeBlock>
                <p class="text-xs text-muted-foreground">
                    With persistent layouts, Inertia keeps the layout instance
                    mounted and only swaps the page slot content. Layout state,
                    intervals, and event listeners all survive navigation.
                </p>
            </div>
        </FeatureCard>
    </div>
</template>
