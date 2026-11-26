<script setup lang="ts">
import { Form, Head } from '@inertiajs/vue3';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import FormContextField from '@/components/FormContextField.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';
import { submitFormComponent } from '@/wayfinder/App/Http/Controllers/Feature/FormController';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Forms' },
    { title: 'useFormContext' },
];
</script>

<template>
    <Head title="useFormContext" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="useFormContext"
                docs="the-basics/forms#form-context"
                controller="app/Http/Controllers/Feature/FormController.php#L90"
            >
                Access parent <code class="text-xs">&lt;Form&gt;</code> state
                from deeply nested child components. No prop drilling needed.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Form with child components -->
                <FeatureCard title="Parent Form">
                    <template #description>
                        The <code class="text-xs">&lt;Form&gt;</code> wraps
                        child
                        <code class="text-xs">FormContextField</code> components
                        that use
                        <code class="text-xs">useFormContext()</code> to access
                        form state.
                    </template>

                    <Form
                        v-bind="submitFormComponent.form()"
                        class="space-y-4"
                        #default="{
                            processing,
                            isDirty,
                            hasErrors,
                            recentlySuccessful,
                            validating,
                        }"
                    >
                        <!-- Hidden default role so FeatureFormRequest passes -->
                        <input type="hidden" name="role" value="developer" />

                        <!-- These are child components using useFormContext() -->
                        <FormContextField
                            name="name"
                            label="Name"
                            placeholder="Enter your name..."
                        />
                        <FormContextField
                            name="email"
                            label="Email"
                            type="text"
                            placeholder="you@example.com"
                        />
                        <FormContextField
                            name="bio"
                            label="Bio"
                            placeholder="Tell us about yourself..."
                        />

                        <div class="flex items-center gap-2 pt-2">
                            <Button type="submit" :disabled="processing">
                                {{ processing ? 'Submitting...' : 'Submit' }}
                            </Button>
                            <span
                                v-if="isDirty"
                                class="text-sm text-muted-foreground"
                                >Unsaved changes</span
                            >
                            <span
                                v-if="recentlySuccessful"
                                class="text-sm text-green-600"
                                >Saved!</span
                            >
                        </div>

                        <!-- Parent State Display -->
                        <div
                            class="mt-4 space-y-3 rounded-md border border-black/10 p-4 dark:border-white/10"
                        >
                            <h3 class="text-sm font-semibold">
                                Parent Slot Props
                            </h3>
                            <div class="grid grid-cols-2 gap-2 text-sm">
                                <div class="flex items-center justify-between">
                                    <span>processing</span>
                                    <Badge
                                        :variant="
                                            processing ? 'default' : 'secondary'
                                        "
                                        class="text-xs"
                                        >{{ processing }}</Badge
                                    >
                                </div>
                                <div class="flex items-center justify-between">
                                    <span>isDirty</span>
                                    <Badge
                                        :variant="
                                            isDirty ? 'default' : 'secondary'
                                        "
                                        class="text-xs"
                                        >{{ isDirty }}</Badge
                                    >
                                </div>
                                <div class="flex items-center justify-between">
                                    <span>hasErrors</span>
                                    <Badge
                                        :variant="
                                            hasErrors
                                                ? 'destructive'
                                                : 'secondary'
                                        "
                                        class="text-xs"
                                        >{{ hasErrors }}</Badge
                                    >
                                </div>
                                <div class="flex items-center justify-between">
                                    <span>validating</span>
                                    <Badge
                                        :variant="
                                            validating ? 'default' : 'secondary'
                                        "
                                        class="text-xs"
                                        >{{ validating }}</Badge
                                    >
                                </div>
                            </div>
                        </div>
                    </Form>
                </FeatureCard>

                <!-- How It Works -->
                <div class="space-y-6">
                    <FeatureCard info-card title="How It Works">
                        <div class="space-y-3 text-sm text-muted-foreground">
                            <p>
                                <strong>1.</strong> The parent renders a
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >&lt;Form&gt;</code
                                >
                                component.
                            </p>
                            <p>
                                <strong>2.</strong> Child components call
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >useFormContext()</code
                                >
                                to access the same form state.
                            </p>
                            <p>
                                <strong>3.</strong> No props need to be passed.
                                Context is provided automatically via Vue's
                                provide/inject.
                            </p>
                            <p>
                                <strong>4.</strong> Children can read
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >errors</code
                                >,
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >processing</code
                                >,
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >isDirty</code
                                >, and call
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >validate()</code
                                >
                                /
                                <code
                                    class="rounded bg-muted px-1 py-0.5 text-xs"
                                    >touch()</code
                                >.
                            </p>
                        </div>
                    </FeatureCard>

                    <FeatureCard info-card title="API Reference">
                        <div class="space-y-3">
                            <CodeBlock
                                title="Child Component"
                                code="import { useFormContext } from '@inertiajs/vue3'

const form = useFormContext()
// form.errors, form.processing, form.isDirty
// form.validate('field'), form.touch('field')"
                            />
                            <CodeBlock title="Returns null outside Form">
                                <textarea>
const form = useFormContext()
// Returns null if not inside a <Form>
                                </textarea>
                            </CodeBlock>
                        </div>
                    </FeatureCard>
                </div>
            </div>
        </div>
    </AppLayout>
</template>
