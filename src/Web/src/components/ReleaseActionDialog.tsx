import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
} from '@fluentui/react-components'
import type { ApiError } from '../api/client'

interface ReleaseActionDialogProps {
  open: boolean
  title: string
  actionLabel: string
  description: string
  targetVersion: string
  pending: boolean
  error: ApiError | null
  onConfirm: () => void
  onClose: () => void
}

export default function ReleaseActionDialog({
  open,
  title,
  actionLabel,
  description,
  targetVersion,
  pending,
  error,
  onConfirm,
  onClose,
}: ReleaseActionDialogProps) {
  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && !pending && onClose()}>
      <DialogSurface aria-describedby="release-confirmation-description">
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent className="confirmation-content">
            <Text id="release-confirmation-description">{description}</Text>
            <div className="confirmation-target" aria-label={`Exact target version ${targetVersion}`}>
              <Text size={200}>Exact target</Text>
              <Text size={600} weight="semibold">{targetVersion}</Text>
            </div>
            {error && (
              <MessageBar intent="error" role="alert">
                <MessageBarBody>
                  <MessageBarTitle>{error.status === 412 ? 'Registry changed' : `${actionLabel} failed`}</MessageBarTitle>
                  {error.status === 412
                    ? 'The registry was updated by someone else. Close this dialog, review the refreshed data, and try again.'
                    : error.message}
                  {error.problem?.correlationId && (
                    <Text block size={200}>Reference: {error.problem.correlationId}</Text>
                  )}
                </MessageBarBody>
              </MessageBar>
            )}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" disabled={pending} onClick={onClose}>Cancel</Button>
            <Button appearance="primary" disabled={pending} onClick={onConfirm}>
              {pending ? <Spinner size="tiny" labelPosition="after" label={`${actionLabel} in progress`} /> : actionLabel}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
